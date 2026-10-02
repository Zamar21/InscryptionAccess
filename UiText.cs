// UiText.cs
using System.Reflection;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// "What string is this component showing?" — asked by reflection, once,
    /// for the whole mod. (0.7.232.)
    ///
    /// WHY REFLECTION. The game draws text with at least four different types:
    /// UnityEngine.UI.Text (in UnityEngine.UI, which the csproj does not
    /// reference), TextMeshPro's TMP_Text, the game's own GBC.PixelText, and
    /// plain TextMesh. A reader that names one of them can only read one of
    /// them, and adding a reference to chase the others is how a build starts
    /// dragging assemblies behind it. Asking any component for a `text` string
    /// reads all four and keeps the reference list exactly as it is — the same
    /// call VideoNarrator makes for UnityEngine.VideoModule.
    ///
    /// This had two private copies before this file existed, in
    /// PauseMenuReader and OptionsReader. They now both call here; the
    /// behaviour is unchanged, including the order the names are tried in,
    /// which is load-bearing (`text` first, because TMP_Text has both `text`
    /// and `Text`).
    /// </summary>
    public static class UiText
    {
        private static readonly string[] Names = { "text", "Text", "displayedText" };

        public static string Of(Component c)
        {
            if (c == null) return null;
            var t = c.GetType();

            foreach (var name in Names)
            {
                try
                {
                    var pr = t.GetProperty(name, BindingFlags.Instance
                                               | BindingFlags.Public
                                               | BindingFlags.NonPublic);
                    if (pr != null && pr.PropertyType == typeof(string))
                    {
                        var v = pr.GetValue(c, null) as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }

                    var f = t.GetField(name, BindingFlags.Instance
                                           | BindingFlags.Public
                                           | BindingFlags.NonPublic);
                    if (f != null && f.FieldType == typeof(string))
                    {
                        var v = f.GetValue(c) as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
