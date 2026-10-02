// UpdateSignature.cs
using System;
using System.Security.Cryptography;
using System.Text;

namespace IKMA
{
    /// <summary>
    /// Is this download really an IKMA release? (Session 32. UNTESTED in the
    /// game; the check itself was tested in the build container.)
    /// </summary>
    /// <remarks>
    /// Used in two places, compiled into both from this one file:
    ///   - AutoUpdate.cs (in the mod), after downloading, before saving.
    ///   - tools\patcher (IKMAUpdater.dll), again, just before it swaps the
    ///     file in at the next game start - the saved file sat on disk in
    ///     between, so it is checked again.
    ///
    /// WHY A SIGNATURE AND NOT JUST A CHECKSUM. A checksum published next to
    /// the file on GitHub proves the file arrived intact, but whoever controls
    /// the GitHub account controls both. A signature can only be made with the
    /// private key, which never goes near GitHub. So a stolen GitHub account
    /// still cannot push a fake update to players' games.
    ///
    /// THE .sig FILE, three lines of plain text:
    ///     IKMA-UPDATE-1
    ///     0.7.366
    ///     base64 signature
    /// The signature covers the first two lines AND the DLL's bytes, so the
    /// version number cannot be edited either: an old genuine release cannot
    /// be relabelled as a new one to push players backwards.
    ///
    /// The maths: RSA, PKCS#1 v1.5 padding, SHA-256 - the long-standing
    /// standard, present in every .NET the game or the release PC runs.
    /// </remarks>
    internal static class UpdateSignature
    {
        internal const string Magic = "IKMA-UPDATE-1";

        /// <summary>The bytes the signature covers: header lines, then the DLL.</summary>
        internal static byte[] SignedBytes(string version, byte[] dll)
        {
            byte[] header = Encoding.UTF8.GetBytes(Magic + "\n" + version + "\n");
            var all = new byte[header.Length + dll.Length];
            Buffer.BlockCopy(header, 0, all, 0, header.Length);
            Buffer.BlockCopy(dll, 0, all, header.Length, dll.Length);
            return all;
        }

        /// <summary>
        /// The version a .sig file CLAIMS, before anything is verified. Used
        /// only to skip a download that would not be newer; Verify decides.
        /// </summary>
        internal static string ClaimedVersion(string sigText)
        {
            string[] lines = Lines(sigText);
            if (lines.Length < 3 || lines[0] != Magic) return null;
            return Parse(lines[1]) != null ? lines[1] : null;
        }

        /// <summary>
        /// True only if the signature was made by IKMA's private key over
        /// exactly this DLL and this version. Any doubt answers false.
        /// </summary>
        internal static bool Verify(byte[] dll, string sigText, out string version, out string why)
        {
            version = null;
            why = null;
            try
            {
                if (string.IsNullOrEmpty(UpdateKey.PublicKeyXml)) { why = "no update key is built into this IKMA"; return false; }
                if (dll == null || dll.Length == 0) { why = "the download is empty"; return false; }

                string[] lines = Lines(sigText);
                if (lines.Length < 3 || lines[0] != Magic) { why = "the signature file is not in IKMA's format"; return false; }
                if (Parse(lines[1]) == null) { why = "the signature file has no valid version"; return false; }

                byte[] signature;
                try { signature = Convert.FromBase64String(lines[2]); }
                catch (FormatException) { why = "the signature is not readable"; return false; }

                byte[] hash;
                using (var sha = SHA256.Create())
                    hash = sha.ComputeHash(SignedBytes(lines[1], dll));

                using (var rsa = RSA.Create())
                {
                    rsa.FromXmlString(UpdateKey.PublicKeyXml);
                    var check = new RSAPKCS1SignatureDeformatter(rsa);
                    check.SetHashAlgorithm("SHA256");
                    if (!check.VerifySignature(hash, signature))
                    {
                        why = "the signature does not match - this file was not made by IKMA's release key";
                        return false;
                    }
                }
                version = lines[1];
                return true;
            }
            catch (Exception e)
            {
                why = "the check could not run: " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// Compare two versions such as 0.7.365 and 0.7.366 number by number (so 0.7.99 is
        /// older than 0.7.100, which a text comparison gets wrong).
        /// Negative: a is older. Zero: same. Positive: a is newer.
        /// A version that will not parse counts as older than anything.
        /// </summary>
        internal static int Compare(string a, string b)
        {
            int[] x = Parse(a), y = Parse(b);
            if (x == null) return y == null ? 0 : -1;
            if (y == null) return 1;
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p < q ? -1 : 1;
            }
            return 0;
        }

        // Digits and dots only, 1 to 6 numbers, like 0.7.365. Anything else is null.
        private static int[] Parse(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            string[] parts = v.Trim().Split('.');
            if (parts.Length < 1 || parts.Length > 6) return null;
            var n = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0 || parts[i].Length > 9) return null;
                foreach (char c in parts[i]) if (c < '0' || c > '9') return null;
                n[i] = int.Parse(parts[i]);
            }
            return n;
        }

        private static string[] Lines(string text)
        {
            if (text == null) return new string[0];
            // A file saved by a Windows editor may start with an invisible
            // byte-order mark (U+FEFF); it is not part of the text.
            string[] raw = text.Replace("\uFEFF", "").Replace("\r", "").Split('\n');
            var list = new System.Collections.Generic.List<string>();
            foreach (string l in raw) if (l.Trim().Length > 0) list.Add(l.Trim());
            return list.ToArray();
        }
    }
}
