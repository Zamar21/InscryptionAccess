// CabinProbe.cs
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// LOG ONLY. Speaks nothing, changes nothing, drives nothing.
    /// The survey that has to come before the cabin's 1, 2 and 3 keys.
    /// (Session 17.)
    ///
    /// WHY A PROBE AND NOT A DUMP. dump_cabin.txt answered the structural
    /// questions and then hit the wall this project already knows: the two
    /// fields that matter are serialized scene data.
    ///
    ///     CabinInteractable.interactableFromZones       NONPUBLIC List
    ///     CabinInteractable.interactableFromDirections  NONPUBLIC List
    ///
    /// Reflection can name a field and can never see inside it with the game
    /// closed. That is exactly where the camera ladder stalled in Session 16,
    /// and the answer there was the same as here: a log-only probe in a build.
    ///
    /// WHY THIS IS NOT A BANNED SCENE SEARCH. FindObjectOfType is out, and the
    /// dump established that nothing owns these objects — CabinManager holds
    /// two walls and two puzzle GameObjects and no interactables at all, so
    /// there is no list to walk from a singleton.
    ///
    /// So the game hands them over instead. InteractableBase.SetEnabled(Boolean)
    /// is PUBLIC, is declared exactly ONCE in the whole assembly, and is called
    /// by the game on each interactable as the cabin sets itself up. The patch
    /// receives __instance. That is a Harmony instance, which is the supported
    /// route this project already uses everywhere else — not a scene scan.
    ///
    /// WHAT THE NEXT BUILD NEEDS FROM THIS LOG:
    ///
    ///   1. WHICH objects exist, and their real types. His descriptions promise
    ///      keys at the clock, the safe, the frame, the door, the shelf and the
    ///      gramophone; only some of those are named types in the dump and the
    ///      picture frame was never identified at all.
    ///   2. WHAT interactableFromZones actually contains. If those are the
    ///      game's own standing spaces, IKMA should key 1/2/3 off the game's
    ///      answer rather than off the hand-measured grid in CabinMap — one
    ///      source for every fact, and the game is the source of truth.
    ///   3. WHAT interactableFromDirections contains, and whether its values
    ///      line up with the four facings the survey measured.
    ///   4. Whether PrerequisitesMet is the gate for "you cannot use this yet".
    ///
    /// ONE LINE PER OBJECT, EVER. SetEnabled is called repeatedly as the cabin
    /// changes state, and the log is a player-facing artifact — this project
    /// has already put 82,761 lines of its own noise into one. Each instance is
    /// logged the first time it is seen and never again.
    /// </summary>
    public static class CabinProbe
    {
        private static ManualLogSource _log;
        public static void Init(ManualLogSource log) => _log = log;

        // Reference identity, so two objects with the same name stay distinct
        // and a re-enabled object is not reported twice.
        private static readonly HashSet<object> _seen = new HashSet<object>();

        private static FieldInfo _zonesField;
        private static FieldInfo _directionsField;
        private static bool      _fieldsResolved;

        /// <summary>Dropped on scene change — a new cabin is a new survey.</summary>
        public static void Reset()
        {
            _seen.Clear();
            _known.Clear();
            _loose.Clear();
            _swept = false;
        }

        private static void ResolveFields()
        {
            if (_fieldsResolved) return;
            _fieldsResolved = true;

            const BindingFlags any = BindingFlags.Instance
                                   | BindingFlags.Public
                                   | BindingFlags.NonPublic;

            _zonesField      = typeof(CabinInteractable).GetField("interactableFromZones", any);
            _directionsField = typeof(CabinInteractable).GetField("interactableFromDirections", any);

            // A missing field is a finding, not a crash. Say so once.
            if (_zonesField == null)
                _log?.LogWarning("IKMA CABINPROBE: interactableFromZones not found on CabinInteractable.");
            if (_directionsField == null)
                _log?.LogWarning("IKMA CABINPROBE: interactableFromDirections not found on CabinInteractable.");
        }

        /// <summary>
        /// Read a list field as text. Prints the ELEMENT TYPE as well as the
        /// values, because the dump could only say "List`1" — what these are
        /// lists OF is one of the questions this probe exists to answer.
        /// </summary>
        private static string DescribeList(FieldInfo field, object owner)
        {
            if (field == null) return "field not found";

            object value;
            try { value = field.GetValue(owner); }
            catch (System.Exception e) { return $"unreadable ({e.GetType().Name})"; }

            if (value == null) return "null";

            var items = value as System.Collections.IEnumerable;
            if (items == null) return $"not enumerable ({value.GetType().Name})";

            var parts = new List<string>();
            string elementType = null;

            foreach (var item in items)
            {
                if (item == null) { parts.Add("null"); continue; }
                if (elementType == null) elementType = item.GetType().Name;

                // A UnityEngine.Object's ToString carries its GameObject name,
                // which is what makes a zone identifiable at all.
                parts.Add(item.ToString());
            }

            if (parts.Count == 0) return "empty";
            return $"[{elementType ?? "?"}] " + string.Join(" | ", parts.ToArray());
        }

        // ==================================================================
        // THE REGISTRY. (Session 17.)
        //
        // Every CabinInteractable the game has handed over, kept so a number key
        // can ask "what is usable from where I am standing, facing this way".
        //
        // It is built from patches and never from a search: the game calls
        // SetEnabled and CursorEnter on these objects itself and the patch
        // receives __instance. Nothing owns the cabin's interactables —
        // CabinManager holds two walls and two puzzle GameObjects, and
        // NavigationZone3D holds events and view offsets but no interactable
        // list, both checked directly — so a registry IKMA builds itself is the
        // only route that does not break the no-scene-search rule.
        //
        // INCOMPLETE BY CONSTRUCTION, and that is why this build only LOGS what
        // it matches. SetEnabled fires during scene setup for a handful of
        // objects; CursorEnter fires for whatever the player points at. Until a
        // log says which objects actually turn up beside which zone, wiring a
        // number key to press one would be guessing at the mapping.
        // ==================================================================
        private static readonly List<CabinInteractable> _known = new List<CabinInteractable>();

        /// <summary>
        /// Log every registered interactable usable from the player's current
        /// zone and facing. Speaks nothing. This is the data the drive half of
        /// the number keys needs before it can be written.
        /// </summary>
        /// <summary>
        /// Every registered interactable usable from the player's current zone
        /// and facing, in a STABLE order.
        ///
        /// Ordered by GameObject name rather than by registration order, because
        /// registration order depends on what the game happened to enable and
        /// what the player happened to point at — it would differ between runs,
        /// and a number key that means a different object on Tuesday is worse
        /// than one that does nothing.
        /// </summary>
        /// <summary>
        /// ASK THE CABIN FOR ITS OWN CONTENTS. (Session 17, and it is the fix
        /// for three separate "didn't work" reports at once.)
        ///
        /// His 0.7.89 log, by zone:
        ///     door    (Middle East / East)  — the door knob never appeared
        ///     cage    (North Middle / North) — only the skull zoom and the globe
        ///     clock   (South Middle / South) — no matches at all
        ///
        /// One cause. The registry was fed by SetEnabled, by CursorEnter and by
        /// ManagedUpdate, and an object reaches it only if the game happens to
        /// call one of those. DoorKnobInteractable declares no ManagedUpdate and
        /// is never enabled or disabled during a run, so nothing ever handed it
        /// over. The clock's knobs are worse: they are MainInputInteractables
        /// owned by a CuckooClock, which is a plain ManagedBehaviour, so they are
        /// not CabinInteractables and the filter excluded them by type.
        ///
        /// So the cabin is asked directly. CabinManager is a Singleton, and
        /// GetComponentsInChildren on its own object is asking ONE object about
        /// its own children — the same justification NodeProbe, PauseProbe and
        /// the zoom sub-controls already use. **It is not FindObjectOfType**;
        /// nothing is searched for, and the object doing the answering is one
        /// the game handed over.
        ///
        /// Run once per cabin, not per frame, and every object logs itself once
        /// — the log is a player-facing artifact and this project has already
        /// filled one with its own noise.
        /// </summary>
        private static bool _swept;

        // ==================================================================
        // THE PER-OBJECT SURVEY LOG IS OFF. (0.7.113.)
        //
        // Zamar, Session 18: "there's a lot of noise in this log I'd prefer not
        // to be here." 218 of 709 lines in his 0.7.112 log were this class
        // naming every interactable in the cabin — 31% of a file he reads with a
        // screen reader.
        //
        // IT HAS DONE ITS JOB. The survey is what found the zone and direction
        // lists, the loose registry, WallFastenedObject_Brush and _Hammer, and
        // GrabbableWolfStatue. Those answers are now written down in CabinMap
        // and in the handoff, so re-printing them every launch buys nothing.
        //
        // WHAT STAYS ON, and why each earns its place:
        //   - the one-line sweep summary, which is how a session confirms the
        //     registry actually filled;
        //   - every warning, because a missing field is a real failure;
        //   - the key-press diagnostics, which are how an unmapped digit gets
        //     mapped — the noisy fallback this project wants noisy.
        //
        // A SOURCE-LEVEL SWITCH, deliberately not a config entry, for the same
        // reason LogFilter.ENABLED is one: turning it back on is a development
        // act, and the next unidentified object in the cabin is what calls for
        // it. Flip to true, build, sweep, flip back.
        //
        // static readonly, NOT const, and that is not a style choice. As a const
        // the compiler folds `if (SURVEY_LOG)` away and reports the body as
        // CS0162 unreachable code — two warnings on a project whose build gate
        // is zero. A static readonly reads the same at every call site, costs
        // nothing the JIT will not fold anyway, and keeps the build clean.
        // ==================================================================
        internal static readonly bool SURVEY_LOG = false;

        public static void SweepCabin()
        {
            if (_swept) return;

            var cabin = Singleton<ExplorableAreaManager>.Instance;
            if (cabin == null) return;

            _swept = true;
            int found = 0, cabinKind = 0;

            try
            {
                foreach (var it in cabin.GetComponentsInChildren<MainInputInteractable>(true))
                {
                    if (it == null) continue;
                    found++;

                    var asCabin = it as CabinInteractable;
                    if (asCabin != null) { Note(asCabin, "sweep"); cabinKind++; }
                    else                 { NoteLoose(it); }
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CABINPROBE: sweep threw {e.GetType().Name}.");
            }

            _log?.LogInfo(
                $"IKMA CABINPROBE: swept the cabin — {found} interactable(s), " +
                $"{cabinKind} with zone data, {_loose.Count} without.");
        }

        /// <summary>
        /// Interactables with NO zone data — the clock's knobs, the gramophone's
        /// parts, anything owned by a plain behaviour rather than by a
        /// CabinInteractable. They cannot be matched to a standing space by the
        /// game's own answer, so they are kept by NAME and reached only through
        /// an explicit mapping Zamar has approved.
        /// </summary>
        private static readonly Dictionary<string, MainInputInteractable> _loose
            = new Dictionary<string, MainInputInteractable>();

        private static void NoteLoose(MainInputInteractable it)
        {
            string nm = null;
            try { nm = it.gameObject.name; } catch { }
            if (string.IsNullOrEmpty(nm)) return;

            string key = nm.Trim().ToLowerInvariant();
            if (_loose.ContainsKey(key)) return;
            _loose[key] = it;

            string pos = "?";
            try
            {
                var p = it.transform.position;
                pos = $"x={p.x:F2} y={p.y:F2} z={p.z:F2}";
            }
            catch { }

            if (SURVEY_LOG)
                _log?.LogInfo(
                    $"IKMA CABINPROBE: [loose] {it.GetType().Name} obj='{nm}' {pos}");
        }

        /// <summary>One of the no-zone interactables, by GameObject name.</summary>
        public static MainInputInteractable Loose(string gameObjectName)
        {
            if (string.IsNullOrEmpty(gameObjectName)) return null;
            MainInputInteractable it;
            return _loose.TryGetValue(gameObjectName.Trim().ToLowerInvariant(), out it) ? it : null;
        }

        public static List<CabinInteractable> MatchesHere()
        {
            var result = new List<CabinInteractable>();

            var fpc = Singleton<FirstPersonController>.Instance;
            if (fpc == null) return result;

            NavigationZone3D zone = null;
            string dir = null;
            try { zone = fpc.CurrentZone; } catch { }
            try { dir  = fpc.LookDirection.ToString(); } catch { }
            if (zone == null || dir == null) return result;

            ResolveFields();

            foreach (var c in _known)
            {
                if (c == null) continue;

                bool zoneOk = false, dirOk = false;
                try
                {
                    var zones = _zonesField?.GetValue(c) as System.Collections.IEnumerable;
                    if (zones != null)
                        foreach (var z in zones) if (ReferenceEquals(z, zone)) { zoneOk = true; break; }

                    var dirs = _directionsField?.GetValue(c) as System.Collections.IEnumerable;
                    if (dirs != null)
                        foreach (var d in dirs) if (d != null && d.ToString() == dir) { dirOk = true; break; }
                }
                catch { }

                if (zoneOk && dirOk) result.Add(c);
            }

            result.Sort((a, b) =>
            {
                string an = "", bn = "";
                try { an = a.gameObject.name; } catch { }
                try { bn = b.gameObject.name; } catch { }
                return string.CompareOrdinal(an, bn);
            });

            return result;
        }

        /// <summary>
        /// Press one of them, through the same primitive card play uses.
        /// CursorSelectStart / CursorSelectEnd are PUBLIC on MainInputInteractable
        /// and virtual-dispatch to each object's own override, so the game
        /// decides what a click on it means. IKMA never reproduces the effect.
        /// </summary>
        public static bool Press(CabinInteractable target)
        {
            if (target == null) return false;

            string nm = "?";
            try { nm = target.gameObject.name; } catch { }

            try
            {
                target.CursorSelectStart();
                target.CursorSelectEnd();
                _log?.LogInfo($"IKMA CABIN: pressed '{nm}' ({target.GetType().Name}).");
                return true;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CABIN: pressing '{nm}' threw {e.GetType().Name}.");
                return false;
            }
        }

        /// <summary>
        /// The controls ON a zoomed-in object — the safe's three dials, the
        /// clock's three knobs. (Session 17.)
        ///
        /// Zamar's spec: "I want to walk up to the safe, have Space tell me 1
        /// for safe 2 for carving, I press 1, it focuses in on the safe, then
        /// have 1/2/3 interact with the safe. I then press backspace to step
        /// back from the safe and can press 1 to refocus on the safe, or 2 to
        /// focus on the wood carving."
        ///
        /// So a number key means two different things depending on whether the
        /// player is zoomed in, and the zoom is a layer above standing.
        ///
        /// ORDERED LEFT TO RIGHT, on world x. Three dials in a row are read the
        /// way they are seen, and this project has already settled that "first"
        /// must mean what a sighted player sees first — the map paths bug twice
        /// over. Ties break on name so the order is stable between runs.
        ///
        /// GetComponentsInChildren on the zoomed object is asking ONE object
        /// about its own children, which is the same justification NodeProbe
        /// uses. It is not the banned scene search.
        /// </summary>
        public static List<MainInputInteractable> SubControls(Component zoomed)
        {
            var result = new List<MainInputInteractable>();
            if (zoomed == null) return result;

            try
            {
                foreach (var c in zoomed.GetComponentsInChildren<MainInputInteractable>(false))
                {
                    if (c == null) continue;

                    // The zoom object itself is not one of its own controls —
                    // pressing it again would just toggle the zoom back off.
                    if (ReferenceEquals(c, zoomed)) continue;

                    bool live = false;
                    try { live = c.gameObject.activeInHierarchy && c.Enabled; } catch { }
                    if (live) result.Add(c);
                }
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CABIN: reading sub-controls threw {e.GetType().Name}.");
            }

            result.Sort((a, b) =>
            {
                float ax = 0f, bx = 0f;
                string an = "", bn = "";
                try { ax = a.transform.position.x; an = a.gameObject.name; } catch { }
                try { bx = b.transform.position.x; bn = b.gameObject.name; } catch { }
                int byX = ax.CompareTo(bx);
                return byX != 0 ? byX : string.CompareOrdinal(an, bn);
            });

            return result;
        }

        /// <summary>Press one of them, through the game's own primitive.</summary>
        public static bool PressSub(MainInputInteractable target)
        {
            if (target == null) return false;

            string nm = "?";
            try { nm = target.gameObject.name; } catch { }

            try
            {
                target.CursorSelectStart();
                target.CursorSelectEnd();
                _log?.LogInfo($"IKMA CABIN: pressed control '{nm}' ({target.GetType().Name}).");
                return true;
            }
            catch (System.Exception e)
            {
                _log?.LogWarning($"IKMA CABIN: pressing control '{nm}' threw {e.GetType().Name}.");
                return false;
            }
        }

        public static void LogWhatIsHere(int digit)
        {
            var fpc = Singleton<FirstPersonController>.Instance;
            if (fpc == null)
            {
                _log?.LogInfo($"IKMA CABINPROBE: key {digit} — no FirstPersonController.");
                return;
            }

            NavigationZone3D zone = null;
            string dir = "?";
            try { zone = fpc.CurrentZone; } catch { }
            try { dir  = fpc.LookDirection.ToString(); } catch { }

            string zoneName = zone == null ? "null" : zone.ToString();
            _log?.LogInfo(
                $"IKMA CABINPROBE: key {digit} pressed — zone='{zoneName}' facing={dir}, " +
                $"{_known.Count} interactable(s) known.");

            ResolveFields();
            int matched = 0;

            foreach (var c in _known)
            {
                if (c == null) continue;

                bool zoneOk = false, dirOk = false;
                try
                {
                    var zones = _zonesField?.GetValue(c) as System.Collections.IEnumerable;
                    if (zones != null && zone != null)
                        foreach (var z in zones) if (ReferenceEquals(z, zone)) { zoneOk = true; break; }

                    var dirs = _directionsField?.GetValue(c) as System.Collections.IEnumerable;
                    if (dirs != null)
                        foreach (var d in dirs) if (d != null && d.ToString() == dir) { dirOk = true; break; }
                }
                catch { }

                if (zoneOk && dirOk)
                {
                    matched++;
                    string prereq = "?";
                    try { prereq = c.PrerequisitesMet.ToString(); } catch { }
                    string nm = "?";
                    try { nm = c.gameObject.name; } catch { }
                    _log?.LogInfo(
                        $"IKMA CABINPROBE:   match {matched}: {c.GetType().Name} obj='{nm}' " +
                        $"PrerequisitesMet={prereq}");
                }
            }

            if (matched == 0)
                _log?.LogInfo("IKMA CABINPROBE:   no registered interactable matches this zone and facing.");
        }

        internal static void Note(InteractableBase interactable, string how)
        {
            if (interactable == null) return;

            // THIS REGISTRY IS FOR THE CABIN. (0.7.104.)
            //
            // The hover patch fires on every MainInputInteractable in the game,
            // so browsing the options panel or a node screen registered dozens
            // of buttons and cards that will never be a cabin object — 87 lines
            // of Zamar's last log, and a registry slowly filling with things
            // MatchesHere can never match.
            //
            // The two screens that cover the cabin are excluded rather than the
            // cabin being detected, because a cabin object hovered by the mouse
            // is exactly what this is for and no test for "is this the cabin"
            // would be more reliable than knowing what is on top of it.
            if (PauseMenuReader.Active || NodeScreenReader.Active) return;

            if (_seen.Contains(interactable)) return;
            _seen.Add(interactable);

            ResolveFields();

            // CABIN SCENE ONLY. The hover route fires on ANYTHING the cursor
            // reaches — cards, slots, bells, menu items — and a battle would
            // bury the survey in objects that have nothing to do with the room.
            // All of Kaycee's Mod reports this one scene name, so it is the same
            // gate the hover patch already uses.
            try
            {
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                    != "Part1_Cabin") return;
            }
            catch { }

            var cabin = interactable as CabinInteractable;
            if (cabin != null && !_known.Contains(cabin)) _known.Add(cabin);

            string typeName = interactable.GetType().Name;
            string objName  = "?";
            string pos      = "?";

            try { objName = interactable.gameObject.name; } catch { }
            try
            {
                var p = interactable.transform.position;
                pos = $"x={p.x:F2} y={p.y:F2} z={p.z:F2}";
            }
            catch { }

            // PrerequisitesMet is PUBLIC and is the game's own answer to "can
            // this be used right now". Sampled here rather than assumed to be
            // the gate — if it is false on objects that clearly work, it means
            // something narrower than it reads.
            // Only a CabinInteractable has this. Anything else is honestly
            // reported as not applicable rather than as unknown — the gramophone
            // is a plain MainInputInteractable and has no zone data at all, and
            // that is a fact about it rather than a failure to read it.
            string prereq = "n/a";
            if (cabin != null)
            {
                prereq = "?";
                try { prereq = cabin.PrerequisitesMet.ToString(); } catch { }
            }

            string enabled = "?";
            try { enabled = interactable.Enabled.ToString(); } catch { }

            if (!SURVEY_LOG) return;

            _log?.LogInfo(
                $"IKMA CABINPROBE: [{how}] {typeName} obj='{objName}' {pos} " +
                $"PrerequisitesMet={prereq} Enabled={enabled}");

            if (cabin != null)
            {
                _log?.LogInfo(
                    $"IKMA CABINPROBE:   zones      = {DescribeList(_zonesField, cabin)}");
                _log?.LogInfo(
                    $"IKMA CABINPROBE:   directions = {DescribeList(_directionsField, cabin)}");
            }
        }
    }

    /// <summary>
    /// InteractableBase.SetEnabled(Boolean) — PUBLIC, declared exactly once in
    /// the assembly (confirmed in dump_cabin.txt section 5's neighbourhood; the
    /// only other SetEnabled belongs to InteractionCursor, a different type and
    /// not an override). One declaration, one patch.
    ///
    /// A Postfix, so the probe never sits in front of the game's own work.
    /// Wrapped whole: a throw inside a Harmony patch on a method the game calls
    /// this often would be a very expensive way to learn nothing.
    ///
    /// *** NO [HarmonyPatch] ATTRIBUTE. THIS IS LOAD BEARING. ***
    ///
    /// It carried one at 0.7.76 and that broke the entire mod. The attribute is
    /// what enrols a class in PatchAll — so PatchAll found it, found no target
    /// method on it (the target is supplied by TryPatch, not by an attribute),
    /// threw "Undefined target method", and **every patch after that point in
    /// PatchAll never registered**. Zamar: "the controls totally broke this
    /// build, nothing is working."
    ///
    /// The hard rule says risky new patches go through TryPatch, OUTSIDE
    /// PatchAll, precisely so one bad target cannot take the rest down. Routing
    /// it through TryPatch and ALSO leaving the attribute on put it in both
    /// places at once and defeated the whole point. Every other TryPatch class
    /// in this project is undecorated; that is not an oversight, it is the
    /// mechanism.
    /// </summary>
    public static class InteractableBase_SetEnabled_Patch
    {
        public static void Postfix(InteractableBase __instance)
        {
            try
            {
                var cabin = __instance as CabinInteractable;
                if (cabin != null) CabinProbe.Note(cabin, "enabled");
            }
            catch { }
        }
    }

    /// <summary>
    /// THE SECOND DISCOVERY ROUTE, and the 0.7.77 log is why it is needed.
    ///
    /// SetEnabled found seven objects and every one of them fired during scene
    /// setup, before Zamar had even stood up. **The cuckoo clock, the safe, the
    /// picture frame, the heavy door and the gramophone never appeared at all**
    /// — the objects his descriptions promise keys on. Two separate reasons,
    /// and both had to be fixed:
    ///
    ///   1. The game never calls SetEnabled on them during a Kaycee's Mod run.
    ///      They sit in whatever state the scene was authored with, so a hook on
    ///      a state CHANGE can never see them.
    ///   2. Not all of them are CabinInteractables. GramophoneInteractable
    ///      derives straight from MainInputInteractable, and the clock's three
    ///      knobs are MainInputInteractables held by a CuckooClock that is a
    ///      plain ManagedBehaviour. The old filter excluded both by type.
    ///
    /// So this catches the cursor ARRIVING on anything at all. It is bounded by
    /// the player: one line per object, the first time it is pointed at.
    ///
    /// WHY THIS IS NOT A SCENE SEARCH. The game raycasts, decides what the
    /// cursor is over, and calls CursorEnter on it. The patch receives that
    /// object as __instance — IKMA never looks for anything.
    ///
    /// A MOUSE SWEEP IS THE POINT, and it is temporary. Nothing owns these
    /// objects, so until dump_navzones says whether a NavigationZone3D lists
    /// its own contents, pointing at them is the only way to learn they exist.
    /// If a zone does list them, this whole route comes out.
    ///
    /// TWO DECLARATIONS. CursorEnter is declared on InteractableBase and
    /// OVERRIDDEN on MainInputInteractable — a patch on the base alone would
    /// never fire for anything the player can actually click. That is the trap
    /// that made the card choice screen dead for six sessions.
    /// </summary>
    public static class InteractableBase_CursorEnter_Patch
    {
        public static void Postfix(InteractableBase __instance)
        {
            try { CabinProbe.Note(__instance, "hovered"); }
            catch { }
        }
    }

    /// <summary>
    /// THE DISCOVERY GAP, AND WHY KEY 1 DID NOTHING AT THE SAFE.
    /// (0.7.82 playtest.)
    ///
    /// His log, at the safe:
    ///     key 1 pressed — 6 interactable(s) known
    ///     key 1 at '3|east' wants 'Safe', which is not among the matches here.
    /// then, after he had moused over it:
    ///     key 1 pressed — 7 interactable(s) known
    ///     pressed 'Safe' (SafeInteractable).
    ///
    /// **The safe existed the whole time. IKMA had simply never been handed
    /// it.** The registry was fed by SetEnabled — which the game never calls on
    /// the safe during a Kaycee's Mod run — and by CursorEnter, which needs a
    /// mouse. So the one key that exists to replace the mouse required the
    /// mouse first.
    ///
    /// ManagedUpdate is the fix. Inscryption ticks its ManagedBehaviours every
    /// frame, and ZoomInteractable and GlobeInteractable both OVERRIDE it — so
    /// the game runs them itself and the patch receives __instance. The safe,
    /// the picture frame, the wardrobe drawers and the globe register within a
    /// frame of the cabin existing, with no mouse and no scene search.
    ///
    /// PER-FRAME IS FINE ONLY BECAUSE Note() RETURNS ON A HASHSET HIT AFTER THE
    /// FIRST SIGHT. Nothing is logged twice and nothing is allocated — this
    /// project has already put 82,761 lines of its own noise into one log.
    /// </summary>
    public static class Interactable_ManagedUpdate_Patch
    {
        public static void Postfix(ManagedBehaviour __instance)
        {
            try
            {
                var cabin = __instance as CabinInteractable;
                if (cabin != null) CabinProbe.Note(cabin, "ticking");
            }
            catch { }
        }
    }
}

// CabinProbe.cs
