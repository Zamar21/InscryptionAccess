// SetupTextTest.cs
//
// Prints every sentence IKMA Setup can say, in one language, so the
// translations can be checked without Windows. (Session 32.) Built and run
// by run_setup_text.sh, which compiles it together with Setup's own source.
//
//   args: --language NAME   (the same switch IKMA_Manager.exe takes)
//
// Each fixed sentence is read through Text's properties (by reflection, so a
// new sentence is picked up with no edit here); each sentence with blanks is
// called once with sample values.

using System;
using System.Reflection;

namespace IKMASetup
{
    static class SetupTextTest
    {
        static int Main(string[] args)
        {
            L.Start(args);
            foreach (PropertyInfo p in typeof(Text).GetProperties(BindingFlags.Static | BindingFlags.NonPublic))
                Console.WriteLine(p.GetValue(null));
            Console.WriteLine(Text.Title("0.7.366"));
            Console.WriteLine(Text.Found(@"C:\Games\Inscryption"));
            Console.WriteLine(Text.StatusLine(Program.Status.On, "0.7.366"));
            Console.WriteLine(Text.StatusLine(Program.Status.On, null));
            Console.WriteLine(Text.StatusLine(Program.Status.Off, "0.7.366"));
            Console.WriteLine(Text.StatusLine(Program.Status.Off, null));
            Console.WriteLine(Text.StatusLine(Program.Status.NotInstalled, null));
            Console.WriteLine(Text.MainMenu(Program.Status.On));
            Console.WriteLine(Text.MainMenu(Program.Status.Off));
            Console.WriteLine(Text.ChooseFor("TITLE"));
            Console.WriteLine(Text.OptionLine(2, "NAME", true));
            Console.WriteLine(Text.SettingSaved("TITLE", "NAME"));
            Console.WriteLine(Text.CustomValue("VALUE"));
            Console.WriteLine(Text.Failed("REASON"));
            Console.WriteLine(Text.BepInExKeptVersion("5.4.23"));
            Console.WriteLine(Text.OtherLoader("LOADER"));
            Console.WriteLine(Text.IkmaUpdatedAt("PATH"));
            Console.WriteLine(Text.IkmaSeveral("LIST"));
            Console.WriteLine(Text.SpeechBackedUp("FILE.dll"));
            Console.WriteLine(Text.Restored("FILE.dll"));
            return 0;
        }
    }
}
