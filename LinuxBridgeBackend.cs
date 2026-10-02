// LinuxBridgeBackend.cs
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IKMA
{
    /// <summary>
    /// Speech for the Steam Deck (and any Linux PC running the game through
    /// Proton): each line goes over a local TCP connection to the IKMA speech
    /// helper, a small Python program running natively on Linux, which speaks
    /// it with speech-dispatcher or espeak-ng. (Session 32. UNTESTED IN GAME,
    /// UNTESTED ON A DECK.)
    ///
    /// WHY A NETWORK CONNECTION, WHEN BOTH PROGRAMS ARE ON ONE MACHINE?
    /// The game is a Windows program running inside Wine. It cannot call Linux
    /// programs or Linux libraries directly - to it, the Linux side does not
    /// exist. But Wine passes network sockets straight through to Linux, so a
    /// connection to 127.0.0.1 (the "loopback" address, meaning "this same
    /// machine") reaches a real Linux program listening there. It is the
    /// simplest door between the two worlds that needs no special permission.
    ///
    /// WHY TCP AND NOT UDP. TCP delivers lines in order and tells us when the
    /// other end has gone away. Speech order is a hard rule on this project
    /// (strict FIFO), and knowing the helper is gone is how Auto mode knows to
    /// fall back. UDP would silently drop and reorder.
    ///
    /// THE PROTOCOL - one line of UTF-8 text per message, ending in "\n":
    ///   HELLO 1              IKMA -> helper, once per connection.
    ///   OK ikma-speech 1 X   helper -> IKMA, the only thing the helper ever
    ///                        sends. X names the engine it found (spd-say,
    ///                        espeak-ng, espeak, or none).
    ///   SAY text             speak after whatever is already queued.
    ///   INTERRUPT text       stop now, drop the helper's queue, speak this.
    ///   STOP                 stop now, drop the queue, say nothing.
    ///   LANG name            (Session 32) the language the following lines
    ///                        are in, by IKMA's name ("French"). Sent once per
    ///                        connection and again whenever it changes, so the
    ///                        helper reads each line with a voice for its
    ///                        language. No reply; an older helper ignores it.
    /// Newlines inside a spoken line are turned into spaces, because a newline
    /// is what ends a message. The helper's side lives in
    /// tools/steamdeck/ikma_speech_helper.py and must be kept in step.
    ///
    /// THE HANDSHAKE IS WHAT MAKES AUTO SAFE. Anything could be listening on
    /// a port. Only a program that answers "OK ikma-speech" to "HELLO" is
    /// treated as the helper; anything else is closed and ignored.
    ///
    /// THREADING. Same rule as every backend: Maintain, Say/TrySay, AfterSay
    /// and QueryBusy run only on SpeechPump's worker, so the socket is only
    /// ever touched by one thread and needs no lock. CurrentEngineName reads
    /// two volatile fields and nothing else. Shutdown runs only after the
    /// worker has stopped.
    /// </summary>
    internal sealed class LinuxBridgeBackend : ISpeechBackend
    {
        // How long to wait for the helper to accept a connection, and then to
        // answer HELLO. On a real loopback a refused connection comes back in
        // well under a millisecond and an accepted one answers in a few, so
        // these only matter if something is badly wrong. They are spent on the
        // worker thread, never the game's.
        private const int CONNECT_TIMEOUT_MS = 300;
        private const int HELLO_TIMEOUT_MS = 500;

        // After a failed connection, do not try again for this long. Maintain
        // runs before every line; without this, a missing helper would cost a
        // connection attempt per line. Five seconds is how long a player who
        // just started the helper waits for IKMA to notice it.
        private const int RETRY_MS = 5000;

        // Bytes on the wire are UTF-8 WITHOUT a byte-order mark. Encoding.UTF8
        // would work for GetBytes, but naming it explicitly keeps anyone from
        // "fixing" it into a BOM-writing stream writer later.
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        private readonly int _port;
        private readonly Action<string> _log;

        private Socket _socket;
        private int _lastAttemptTick;
        private bool _attempted;

        // Log a "not answering" line once per outage, not once per retry.
        private bool _downLogged;

        // Read from the main thread by CurrentEngineName.
        private volatile bool _connected;
        private volatile string _helperEngine;

        // The language last told to the helper on THIS connection; null = not
        // yet told. Touched only on SpeechPump's worker, like the socket.
        private string _sentLanguage;

        internal LinuxBridgeBackend(int port, Action<string> log)
        {
            _port = port;
            _log = log ?? (_ => { });
        }

        public string Name { get { return "Linux bridge"; } }

        internal bool IsConnected { get { return _connected; } }

        public void Maintain()
        {
            if (!_connected) TryConnect();
        }

        /// <summary>
        /// Plain LinuxBridge mode: a line the helper cannot take is dropped, and
        /// the log says so once. There is nothing else to send it to - this
        /// setting is the player saying "only the helper". Auto mode uses
        /// TrySay instead and falls back.
        /// </summary>
        public void Say(string text, bool interrupt)
        {
            TrySay(text, interrupt);
        }

        /// <summary>
        /// Send one line. True when it was handed to the helper; false when the
        /// helper is not there (and cannot be reached right now).
        /// </summary>
        internal bool TrySay(string text, bool interrupt)
        {
            string message;
            if (string.IsNullOrEmpty(text))
            {
                // The empty interrupting line is how SpeechPump says "stop"
                // (CardReader.Silence, the background hold). An empty
                // NON-interrupting line has nothing to say and nothing to cut.
                if (!interrupt) return true;
                message = "STOP";
            }
            else
            {
                message = (interrupt ? "INTERRUPT " : "SAY ") + OneLine(text);
            }

            if (!TryConnect()) return false;
            if (SendLanguage() && Send(message)) return true;

            // The helper may have restarted since the last line: the old
            // connection is dead but a new one might work. One fresh attempt,
            // straight away, ignoring the retry wait - then give up on this line.
            _attempted = false;
            return TryConnect() && SendLanguage() && Send(message);
        }

        /// <summary>
        /// Tell the helper the language of the lines that follow, if it has
        /// not been told on this connection or the language has changed since.
        /// Loc.SpokenLanguage is the language IKMA last built a line in (a
        /// plain field, safe to read from this thread). False only if the
        /// connection failed while sending.
        /// </summary>
        private bool SendLanguage()
        {
            string lang = Loc.SpokenLanguage;
            if (lang == _sentLanguage) return true;
            if (!Send("LANG " + lang)) return false;
            _sentLanguage = lang;
            return true;
        }

        public void AfterSay() { }

        // Not asked of the helper yet, so Speech.cs uses its word estimate -
        // exactly as it does for any screen reader that does not report busy.
        // Adding a "BUSY?" message to the protocol is listed as a follow-up in
        // WEEKEND_NOTES.md; it would change timing, so it waits for a decision.
        public int QueryBusy() { return -1; }

        public string CurrentEngineName()
        {
            return _connected
                ? $"Linux bridge ({_helperEngine ?? "unknown engine"})"
                : "Linux bridge (not connected)";
        }

        public void Shutdown()
        {
            Close();
        }

        // -----------------------------------------------------------------

        /// <summary>
        /// Connect and handshake if not already connected, respecting the retry
        /// wait. True when a live, verified helper is on the other end.
        /// </summary>
        internal bool TryConnect()
        {
            if (_connected && !PeerHasClosed()) return true;
            if (_connected)
            {
                // It was up and has gone away since the last line.
                Close();
                _log($"IKMA SPEECH: Linux bridge closed by the helper on 127.0.0.1:{_port}.");
                _attempted = false;   // try again straight away, once
            }

            // Environment.TickCount wraps every 49 days; unchecked subtraction
            // gives the right elapsed time across the wrap. Same idiom as
            // SpeechPump.
            if (_attempted && unchecked(Environment.TickCount - _lastAttemptTick) < RETRY_MS)
                return false;
            _attempted = true;
            _lastAttemptTick = Environment.TickCount;

            Socket s = null;
            try
            {
                s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                // Nagle's algorithm holds small writes back for up to ~40ms to
                // batch them into one packet. Every message here is small, and
                // latency is the only thing that matters, so turn it off.
                s.NoDelay = true;

                // Connect with a timeout. Socket.Connect has none of its own,
                // so start it asynchronously and wait on the handle.
                IAsyncResult ar = s.BeginConnect(IPAddress.Loopback, _port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(CONNECT_TIMEOUT_MS))
                    throw new TimeoutException("connect timed out");
                s.EndConnect(ar);

                // The handshake. Anything that is not the helper fails here.
                s.SendTimeout = HELLO_TIMEOUT_MS;
                s.ReceiveTimeout = HELLO_TIMEOUT_MS;
                s.Send(Utf8.GetBytes("HELLO 1\n"));
                string reply = ReadLine(s, HELLO_TIMEOUT_MS);
                if (reply == null || !reply.StartsWith("OK ikma-speech", StringComparison.Ordinal))
                    throw new InvalidOperationException("no helper handshake");

                // "OK ikma-speech 1 spd-say" -> "spd-say".
                string[] parts = reply.Split(' ');
                _helperEngine = parts.Length >= 4 ? parts[3] : null;

                // From here on, a send that cannot complete in 2s means the
                // helper is wedged. Bounded, so the worker cannot hang forever.
                s.SendTimeout = 2000;
                _socket = s;
                _connected = true;
                _sentLanguage = null;   // a new connection is told again
                _downLogged = false;
                _log($"IKMA SPEECH: Linux bridge connected on 127.0.0.1:{_port} (helper engine: {_helperEngine ?? "unknown"}).");
                if (_helperEngine == "none")
                    _log("IKMA SPEECH: the helper found no speech program (spd-say or espeak-ng). It will accept lines but cannot speak them.");
                return true;
            }
            catch (Exception e)
            {
                try { s?.Close(); } catch { }
                if (!_downLogged)
                {
                    _downLogged = true;
                    _log($"IKMA SPEECH: Linux bridge not answering on 127.0.0.1:{_port} ({e.GetType().Name}). " +
                         $"Retrying every {RETRY_MS / 1000}s.");
                }
                return false;
            }
        }

        private bool Send(string message)
        {
            try
            {
                byte[] bytes = Utf8.GetBytes(message + "\n");
                int sent = 0;
                while (sent < bytes.Length)
                    sent += _socket.Send(bytes, sent, bytes.Length - sent, SocketFlags.None);
                return true;
            }
            catch (Exception e)
            {
                Close();
                _log($"IKMA SPEECH: Linux bridge lost ({e.GetType().Name}).");
                return false;
            }
        }

        /// <summary>
        /// Has the helper closed its end? The helper never sends anything after
        /// the handshake, so "readable, with zero bytes waiting" can only mean
        /// the connection was closed. Poll(0) does not wait. This is how a
        /// helper restart is noticed BEFORE a line is lost into the dead socket
        /// (TCP only reports a dead peer on the send after next).
        /// </summary>
        private bool PeerHasClosed()
        {
            try
            {
                return _socket == null
                    || (_socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0);
            }
            catch { return true; }
        }

        /// <summary>
        /// Read bytes until "\n", or give up. Only used for the one-line
        /// handshake reply, so a byte at a time is fine.
        /// </summary>
        private static string ReadLine(Socket s, int timeoutMs)
        {
            var buffer = new byte[1];
            var line = new System.Collections.Generic.List<byte>(64);
            int start = Environment.TickCount;
            while (line.Count < 256)
            {
                if (unchecked(Environment.TickCount - start) > timeoutMs) return null;
                int n = s.Receive(buffer, 0, 1, SocketFlags.None);
                if (n <= 0) return null;          // closed before a full line
                if (buffer[0] == (byte)'\n') break;
                if (buffer[0] != (byte)'\r') line.Add(buffer[0]);
            }
            return Utf8.GetString(line.ToArray());
        }

        private static string OneLine(string text)
        {
            return text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }

        private void Close()
        {
            _connected = false;
            Socket s = _socket;
            _socket = null;
            if (s == null) return;
            try { s.Shutdown(SocketShutdown.Both); } catch { }
            try { s.Close(); } catch { }
        }
    }
}
