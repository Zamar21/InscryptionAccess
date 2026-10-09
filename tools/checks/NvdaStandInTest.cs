// NvdaStandInTest.cs
//
// Runs NvdaDirect.cs against a stand-in for NVDA, with no Windows and no
// NVDA. (Session 50.) Built and run by run_nvda_standin.sh, which compiles
// it together with the mod's own NvdaDirect.cs (define IKMA_TEST).
//
// The stand-in (FakeNvda) is modelled on NVDA's own source, releases 2024.1
// and 2026.2: NVDAHelper nvdaController_speakSsml, speech.speak and
// speech.cancelSpeech. It is a model. A pass here says the logic in
// NvdaDirect.cs does what it was written to do; it does not say NVDA
// behaves as the model does. Only a session with NVDA says that.
//
// Seventeen cases: one line, queued lines, interrupt, bare stop, an
// interrupt with nothing in the air, a call that never returns (NVDA
// 2024.1), speech mode off, NVDA older than 2024.1, NVDA closing mid-line,
// a line NVDA cannot parse, a key press racing the hand-over, an old DLL
// beside the game, SSML text, forty fast interrupts, a long pause, sleep
// mode, and the setting.
using System; using System.Collections.Concurrent; using System.Collections.Generic; using System.Diagnostics; using System.Linq; using System.Threading;
using IKMA;

// A stand-in for NVDA, modelled on NVDA's own source (NVDAHelper nvdaController_speakSsml,
// speech.speak, speech.cancelSpeech): one main thread works through an event queue; a synth
// speaks queued utterances; "done" fires when the synth runs dry; "cancelled" fires from cancelSpeech
// unless speech was already cancelled or speech mode is off.
sealed class FakeNvda : INvdaCalls {
  public bool RegisterUpFront = true;   // 2026.2 behaviour; false = 2024.1
  public volatile bool Old, Dead, SpeechOff, Paused, NoEntryPoint, Sleep;
  public int EventLatencyMs = 3; public double WordsPerSec = 6;
  public int CancelCalls, SsmlCalls, TextCalls, ProbeCalls;
  public readonly List<string> Heard = new List<string>();   // "text|done" or "text|cut"
  sealed class Utter { public string Text; public Action OnStart; public int DurMs; public long EndAt = -1; }
  readonly object lk = new object(); readonly Queue<(long due, Action act)> events = new Queue<(long, Action)>();
  readonly List<Utter> synthQ = new List<Utter>(); Utter cur; bool beenCanceled = true; long pausedAt;
  readonly List<Action> doneHandlers = new List<Action>(); readonly List<Action> cancelHandlers = new List<Action>();
  readonly Thread main; volatile bool stop; static long Now() => Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
  public FakeNvda() { main = new Thread(Loop) { IsBackground = true }; main.Start(); }
  public void Stop() { stop = true; }
  void Post(Action a) { lock (lk) events.Enqueue((Now() + EventLatencyMs, a)); }
  void Loop() { while (!stop) { Action a = null; lock (lk) { if (events.Count > 0 && events.Peek().due <= Now()) a = events.Dequeue().act; }
      if (a != null) { a(); continue; } Tick(); Thread.Sleep(1); } }
  void Tick() { List<Action> fire = null; lock (lk) { if (Paused) return; long now = Now();
      if (cur != null && now >= cur.EndAt) { Heard.Add(cur.Text + "|done"); cur = null; if (synthQ.Count == 0) fire = doneHandlers.ToList(); }
      if (cur == null && synthQ.Count > 0) { cur = synthQ[0]; synthQ.RemoveAt(0); cur.EndAt = now + cur.DurMs; var s = cur.OnStart; if (s != null) { Monitor.Exit(lk); try { s(); } finally { Monitor.Enter(lk); } } } }
    if (fire != null) foreach (var f in fire) f(); }
  int Dur(string t) => (int)(t.Split(' ').Length * 1000 / WordsPerSec);
  void Speak(string text, Action onStart) { if (SpeechOff) return; lock (lk) { beenCanceled = false; synthQ.Add(new Utter { Text = text, OnStart = onStart, DurMs = Dur(text) }); } }
  void Cancel() { List<Action> fire; lock (lk) { if (beenCanceled) return; if (SpeechOff) return; if (cur != null) Heard.Add(cur.Text + "|cut"); foreach (var u in synthQ) Heard.Add(u.Text + "|neverstarted"); cur = null; synthQ.Clear(); fire = cancelHandlers.ToList(); beenCanceled = true; if (Paused) { Paused = false; } }
    foreach (var f in fire) f(); }
  public void PressKey() => Post(Cancel);                       // NVDA cancels speech on a key press
  public void NvdaSaysOwn(string text) => Post(() => Speak(text, null));
  public void Pause() { lock (lk) { Paused = true; pausedAt = Now(); } }
  public void Resume() { lock (lk) { if (!Paused) return; long d = Now() - pausedAt; if (cur != null) cur.EndAt += d; Paused = false; } }
  public void Die() { Dead = true; List<Action> all; lock (lk) { all = dieHandlers.ToList(); } foreach (var a in all) a(); }
  readonly List<Action> dieHandlers = new List<Action>();
  public int GetProcessId(out uint pid) { Interlocked.Increment(ref ProbeCalls); pid = 0; if (NoEntryPoint) throw new EntryPointNotFoundException(); if (Dead) return 1722; if (Old) return 1717; pid = 4242; return 0; }
  public int CancelSpeech() { Interlocked.Increment(ref CancelCalls); if (Dead) return 1722; Post(Cancel); return 0; }
  public int SpeakText(string text) { Interlocked.Increment(ref TextCalls); if (Dead) return 1722; if (Sleep) return -1; Post(() => Speak(text, null)); return 0; }
  public int SpeakSsmlAndWait(string ssml) { Interlocked.Increment(ref SsmlCalls);
    if (Dead) return 1722; if (Old) return 1717; if (Sleep) return 5; if (ssml.Contains("[[BADXML]]")) return 87;
    if (!ssml.StartsWith("<speak>") || !ssml.EndsWith("</speak>")) return 87;
    string text = ssml.Substring(7, ssml.Length - 15).Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
    var q = new BlockingCollection<object>(); object DONE = new object(), CUT = new object(), DIED = new object();
    Action onDone = () => q.Add(DONE), onCut = () => q.Add(CUT), onDie = () => q.Add(DIED);
    Action prefix = () => { lock (lk) { doneHandlers.Add(onDone); if (!RegisterUpFront) cancelHandlers.Add(onCut); } };
    lock (lk) { if (RegisterUpFront) cancelHandlers.Add(onCut); dieHandlers.Add(onDie); }
    Post(() => Speak(text, prefix));
    try { object r = q.Take(); return r == DONE ? 0 : r == CUT ? 1223 : 1726; }
    finally { lock (lk) { doneHandlers.Remove(onDone); cancelHandlers.Remove(onCut); dieHandlers.Remove(onDie); } } }
  public void ReleaseHung() { List<Action> d; lock (lk) d = dieHandlers.ToList(); foreach (var a in d) a(); }  // lets stuck calls return (as 1726)
  public string HeardLine() { lock (lk) return string.Join(" ; ", Heard); }
}

static class T {
  static int fails; static FakeNvda nv; static readonly ConcurrentQueue<string> log = new ConcurrentQueue<string>();
  static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) fails++; }
  static void Fresh(Action<FakeNvda> setup = null) { nv?.Stop(); nv?.ReleaseHung(); Thread.Sleep(60); nv = new FakeNvda(); setup?.Invoke(nv); while (log.TryDequeue(out _)) { } NvdaDirect.ResetForTest(nv, l => log.Enqueue(l)); NvdaDirect.Maintain(true); }
  static void Say(string t, bool interrupt = false) { NvdaDirect.Maintain(true); if (NvdaDirect.Takes) NvdaDirect.Say(t, interrupt); else sentOld.Add(t); }
  static readonly List<string> sentOld = new List<string>();
  // Poll like the pump does (QueryBusy every so often) until cond or timeout.
  static bool Until(Func<bool> cond, int ms) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; NvdaDirect.QueryBusy(); Thread.Sleep(10); } return cond(); }
  static long BusyFor(int maxMs) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < maxMs && NvdaDirect.QueryBusy() != 0) Thread.Sleep(5); return sw.ElapsedMilliseconds; }
  static string Logs() { return string.Join("\n      ", log.ToArray()); }

  static int Main() {
    Console.WriteLine("T1 one line: busy until NVDA's own end, then quiet");
    Fresh(); Check(NvdaDirect.Takes, "takes lines on NVDA 2024.1+"); Check(log.Any(l => l.Contains("reports the end of each line")), "says so once in the log");
    Say("one two three four five six"); Thread.Sleep(50); Check(NvdaDirect.QueryBusy() == 1, "busy 50 ms in");
    long b = BusyFor(5000); Check(b > 850 && b < 1250, $"busy cleared {b + 50} ms after hand-over (line lasts ~1000)"); Check(nv.HeardLine() == "one two three four five six|done", "heard once, whole: " + nv.HeardLine()); Check(nv.CancelCalls == 0, "no stop sent");

    Console.WriteLine("T2 three queued lines keep their order, busy throughout");
    Fresh(); Say("a a a"); Say("b b b"); Say("c c c"); bool gap = false; var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < 1300) { if (NvdaDirect.QueryBusy() != 1) gap = true; Thread.Sleep(5); }
    Check(!gap, "never reported quiet between the lines"); BusyFor(3000); Check(nv.HeardLine() == "a a a|done ; b b b|done ; c c c|done", nv.HeardLine());

    Console.WriteLine("T3 interrupt mid-line cuts it and the new line follows at once");
    Fresh(); Say("long long long long long long long long"); Thread.Sleep(250); sw.Restart(); Say("new line", true);
    Check(Until(() => nv.HeardLine().Contains("|cut"), 300), "old line cut"); Check(nv.CancelCalls == 1, "one stop sent");
    Check(Until(() => nv.HeardLine().EndsWith("new line|done"), 1500), "new line heard whole: " + nv.HeardLine()); Check(NvdaDirect.StuckForTest == 0, "nothing stuck"); Check(BusyFor(500) < 200, "quiet after it");

    Console.WriteLine("T4 a bare stop");
    Fresh(); Say("long long long long long long long long"); Thread.Sleep(200); NvdaDirect.Say("", true); Check(NvdaDirect.QueryBusy() == 0, "quiet immediately after the stop"); Thread.Sleep(100); Check(nv.HeardLine().EndsWith("|cut") && nv.CancelCalls == 1, nv.HeardLine());

    Console.WriteLine("T5 interrupting line when nothing of IKMA's is in the air: no stop is sent, end still exact");
    Fresh(); Say("a a a"); BusyFor(3000); Say("x x x x x x", true); Thread.Sleep(30); Check(nv.CancelCalls == 0, "no stop sent"); b = BusyFor(3000); Check(b > 800 && b < 1200, $"busy {b} ms for a ~1000 ms line"); Check(nv.HeardLine() == "a a a|done ; x x x x x x|done", nv.HeardLine());

    Console.WriteLine("T6 NVDA 2024.1: line cancelled before it started never returns; the next interrupting line is not held up");
    Fresh(n => n.RegisterUpFront = false); nv.NvdaSaysOwn("nvda own speech own speech own speech own speech"); Thread.Sleep(40); Say("ikma line behind"); Thread.Sleep(60); nv.PressKey(); Thread.Sleep(60);
    sw.Restart(); Say("after the key", true); Check(Until(() => nv.HeardLine().EndsWith("after the key|done"), 2500), "next line heard: " + nv.HeardLine()); Check(sw.ElapsedMilliseconds < 1300, $"within {sw.ElapsedMilliseconds} ms (400 wait + ~500 line)"); Check(NvdaDirect.StuckForTest == 1, "one call counted stuck"); Check(BusyFor(600) < 200, "quiet after it"); Check(NvdaDirect.Takes, "still on");

    Console.WriteLine("T7 speech mode off in NVDA: three lines never end, IKMA stands down and hands lines back; resumes when they return");
    Fresh(n => n.SpeechOff = true); Say("a"); Say("b"); Say("c"); Say("d"); Say("e"); sw.Restart();
    Check(Until(() => { NvdaDirect.Maintain(true); return !NvdaDirect.Takes; }, 16000), $"stood down after {sw.ElapsedMilliseconds} ms"); var hb = NvdaDirect.TakeHandBack(); Check(hb != null && string.Join(",", hb) == "d,e", "handed back in order: " + (hb == null ? "null" : string.Join(",", hb))); Check(NvdaDirect.QueryBusy() == 0, "not reporting busy forever");
    nv.SpeechOff = false; nv.ReleaseHung(); Check(Until(() => { NvdaDirect.Maintain(true); return NvdaDirect.Takes; }, 1500), "takes lines again once the stuck calls came back"); Say("f f f"); BusyFor(3000); Check(nv.HeardLine().EndsWith("f f f|done"), nv.HeardLine());

    Console.WriteLine("T8 NVDA older than 2024.1");
    Fresh(n => n.Old = true); Check(!NvdaDirect.Takes, "does not take lines"); for (int i = 0; i < 50; i++) NvdaDirect.Maintain(true); Check(nv.ProbeCalls == 1, "asked once, not per line: " + nv.ProbeCalls); Check(log.Count(l => l.Contains("older than 2024.1")) == 1, "one log line"); Check(nv.SsmlCalls == 0, "never called speakSsml");

    Console.WriteLine("T9 NVDA closes mid-line");
    Fresh(); Say("a a a a a a a a"); Say("b b"); Thread.Sleep(150); nv.Die(); Check(Until(() => !NvdaDirect.Takes, 500), "stops taking lines"); hb = NvdaDirect.TakeHandBack(); Check(hb != null && string.Join("|", hb) == "a a a a a a a a|b b", "both lines handed back: " + (hb == null ? "null" : string.Join("|", hb))); int p = nv.ProbeCalls; for (int i = 0; i < 20; i++) NvdaDirect.Maintain(true); Check(nv.ProbeCalls == p, "no re-check inside 30 s"); Check(!NvdaDirect.Engaged, "not engaged");

    Console.WriteLine("T10 a line NVDA cannot parse is spoken as plain text");
    Fresh(); Say("[[BADXML]] hello there"); Check(Until(() => nv.TextCalls == 1, 500), "speakText used"); Thread.Sleep(30); Check(NvdaDirect.QueryBusy() == -1, "answers cannot-tell while it is read"); Check(Until(() => nv.HeardLine() == "[[BADXML]] hello there|done", 1500), nv.HeardLine()); Check(Until(() => NvdaDirect.QueryBusy() == 0, 2500), "then quiet");

    Console.WriteLine("T11 a key press racing the hand-over (2026.2): line is spoken though reported cancelled; IKMA answers cannot-tell");
    Fresh(n => n.EventLatencyMs = 40); Say("a a a"); BusyFor(3000); nv.NvdaSaysOwn("x"); Thread.Sleep(120); nv.PressKey(); Thread.Sleep(5); Say("raced line raced line"); Thread.Sleep(150);
    Check(NvdaDirect.QueryBusy() == -1, "cannot-tell, not quiet: " + NvdaDirect.QueryBusy()); Check(Until(() => nv.HeardLine().EndsWith("raced line raced line|done"), 1500), nv.HeardLine());

    Console.WriteLine("T12 an old nvdaControllerClient.dll beside the game");
    Fresh(n => n.NoEntryPoint = true); for (int i = 0; i < 10; i++) NvdaDirect.Maintain(true); Check(!NvdaDirect.Takes && nv.ProbeCalls == 1, "off for the run after one try"); Check(log.Count == 1 && log.First().Contains("older copy"), "one log line: " + Logs());

    Console.WriteLine("T13 SSML text");
    Check(NvdaDirect.ToSsml("Elk Fawn's 2/4 & <b> \u0001x") == "<speak>Elk Fawn's 2/4 &amp; &lt;b&gt;  x</speak>", NvdaDirect.ToSsml("Elk Fawn's 2/4 & <b> \u0001x"));

    Console.WriteLine("T14 forty interrupting lines 25 ms apart");
    Fresh(); for (int i = 0; i < 40; i++) { Say("hover line number " + i, true); Thread.Sleep(25); } Check(Until(() => nv.HeardLine().EndsWith("hover line number 39|done"), 3000), "last line heard whole"); Check(NvdaDirect.StuckForTest == 0, "nothing stuck"); Check(nv.HeardLine().Split(';').Count(x => x.EndsWith("|done")) == 1, "only the last one finished"); Check(BusyFor(500) < 200, "quiet after");

    Console.WriteLine("T15 player pauses NVDA for a long time, then resumes");
    Fresh(); Say("p p p"); Thread.Sleep(100); nv.Pause(); Check(Until(() => NvdaDirect.StuckForTest == 1, 9000), "gave up waiting"); Check(NvdaDirect.QueryBusy() == 0 && NvdaDirect.Takes, "quiet, still on"); nv.Resume(); Check(Until(() => NvdaDirect.StuckForTest == 0, 1500), "call came back, nothing stuck"); Check(nv.HeardLine() == "p p p|done", nv.HeardLine());

    Console.WriteLine("T16 sleep mode: NVDA refuses the line; nothing hangs");
    Fresh(n => n.Sleep = true); Say("s s s"); Say("t t t"); Check(Until(() => NvdaDirect.QueryBusy() == 0, 500), "quiet"); Check(NvdaDirect.Takes && NvdaDirect.TakeHandBack() == null, "still on, nothing handed back");

    Console.WriteLine("T17 setting off, and engine not NVDA");
    Fresh(); NvdaDirect.SettingOn = false; NvdaDirect.Maintain(true); Check(!NvdaDirect.Takes, "setting off"); NvdaDirect.SettingOn = true; NvdaDirect.Maintain(false); Check(!NvdaDirect.Takes, "engine is not NVDA"); NvdaDirect.Maintain(true); Check(NvdaDirect.Takes, "back on");

    nv.Stop(); Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED"); return fails; }
}
// NvdaStandInTest.cs
