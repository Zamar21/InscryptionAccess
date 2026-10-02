// CabinMap.cs
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace IKMA
{
    /// <summary>
    /// The Kaycee's Mod cabin, as a grid. (Session 16.)
    ///
    /// THE SPACES ARE MEASURED, NOT GUESSED. Zamar stood in every space and
    /// spun in each one, and the survey lines in his 0.7.65 log give the whole
    /// room. Seven standing positions, four facings each — which matches what he
    /// said before the log was read: "In Kaycee's Mod there are only 7 spaces to
    /// stand, each with 4 directions to face."
    ///
    ///   x is -0.75, 9.25 or 19.25, ten units apart
    ///   z is -10, 0 or 10, ten units apart
    ///   y is always 9.50 while standing still
    ///
    /// A three by three grid with the two spaces at (-0.75, 0) and (-0.75, 10)
    /// missing, which is seven. Headings are exactly 0, 90, 180 and 270.
    ///
    /// THE ONE ANOMALY IN THE DATA, and why the sampler waits: one line reads
    /// x=0.38 y=10.50 z=-4.00 — a sample caught mid-step, with a y that matches
    /// no resting position. Anything whose y is not 9.50 is the camera in
    /// flight, and the nearest-space lookup below tolerates it rather than
    /// snapping to a neighbour.
    ///
    /// WHAT IS DELIBERATELY NOT HERE: any description of the room. Every word a
    /// player hears about the cabin is Zamar's to write — see the wording rule.
    /// This file supplies the STRUCTURE, the 28 slots, and nothing else. Until
    /// he fills them in, the honest answer is the line he already gave:
    /// "Nothing is directly in front of you."
    /// </summary>
    public static class CabinMap
    {
        private static ManualLogSource _log;
        public static void Init(ManualLogSource log) => _log = log;

        // Resting height. A sample away from this is mid-step.
        private const float STANDING_Y   = 9.50f;
        private const float Y_TOLERANCE  = 0.30f;

        // How close a sample has to be to a known space to count as being in it.
        // Spaces are ten units apart, so half of that is unambiguous.
        private const float SPACE_RADIUS = 4.0f;

        public sealed class Space
        {
            public readonly int Id;
            public readonly float X;
            public readonly float Z;

            public Space(int id, float x, float z) { Id = id; X = x; Z = z; }
        }

        // Ordered nearest-the-table first, then left to right, so the numbering
        // follows the way a player walks the room rather than the order the
        // survey happened to record them.
        private static readonly Space[] SPACES =
        {
            new Space(1, -0.75f, -10f),
            new Space(2,  9.25f, -10f),
            new Space(3, 19.25f, -10f),
            new Space(4,  9.25f,   0f),
            new Space(5, 19.25f,   0f),
            new Space(6,  9.25f,  10f),
            new Space(7, 19.25f,  10f),
        };

        public static int SpaceCount => SPACES.Length;

        /// <summary>
        /// Which space the player is standing in, or null when the sample is
        /// mid-step or somewhere the survey never saw. Null is a real answer and
        /// is never rounded away — a wrong space would mean reading out a
        /// description of somewhere the player is not.
        /// </summary>
        public static Space SpaceAt(Vector3 p)
        {
            if (Mathf.Abs(p.y - STANDING_Y) > Y_TOLERANCE) return null;

            Space best = null;
            float bestDistance = float.MaxValue;

            foreach (var s in SPACES)
            {
                float dx = p.x - s.X;
                float dz = p.z - s.Z;
                float d  = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < bestDistance) { bestDistance = d; best = s; }
            }

            return bestDistance <= SPACE_RADIUS ? best : null;
        }

        /// <summary>
        /// The four facings, as the survey found them. Returns null for a
        /// heading between two of them, which means the turn is still playing.
        /// </summary>
        public static string FacingAt(float heading)
        {
            float h = heading % 360f;
            if (h < 0f) h += 360f;

            if (h < 20f || h > 340f) return "north";
            if (h > 70f  && h < 110f) return "east";
            if (h > 160f && h < 200f) return "south";
            if (h > 250f && h < 290f) return "west";
            return null;
        }


        // ==================================================================
        // WALKING BACK TO THE TABLE. (Session 17.)
        //
        // WHY THIS EXISTS. Backspace sits the player down, and it worked only
        // at the table — every attempt from anywhere else logged "LookUp did
        // not move the camera from FirstPerson, either way of pressing it".
        //
        // THAT REFUSAL IS THE GAME BEING RIGHT. Zamar confirmed it from his own
        // play: a sighted player cannot sit down from across the room either;
        // you walk back to the table first. GameFlowManager exposes a PUBLIC
        // TransitionFromFirstPerson that would almost certainly force it, and
        // calling it would be IKMA overruling a refusal the game meant — the
        // exact shape that cost this project three softlocks on the camera
        // ladder. So the mod does what a player does: it walks back, then
        // presses the same button that already works from there.
        //
        // THE MOVEMENT MODEL IS READ OFF HIS 0.7.72 LOG, NOT ASSUMED.
        // Every claim below is a line in that file:
        //
        //   heading 0 = north = +z      heading 90  = east = +x
        //   heading 180 = south = -z    heading 270 = west = -x
        //
        //   ONE DirUp PRESS IS EXACTLY ONE SPACE, and it moves along the
        //   direction the player is FACING. Confirmed at all four headings:
        //   two presses facing east carried him from x=-0.75 to x=19.25; two
        //   facing north from z=-10 to z=10; one facing west 19.25 -> 9.25;
        //   one facing south z=10 -> z=0.
        //
        //   A BLOCKED STEP IS SILENT. "step right" into the east wall left the
        //   position completely unchanged and said nothing. So a route can
        //   never trust a press count — it has to re-read the position after
        //   every press, which is what the walker does.
        //
        // ONLY CONFIRMED PRIMITIVES ARE USED. The walk turns to face a
        // direction and then steps forward, because DirUp is the one movement
        // button whose frame of reference his log establishes at every heading.
        // DirLeft and DirRight would halve the presses and are almost certainly
        // sideways relative to facing — but "almost certainly" is how this
        // project has walked players into walls before. What would settle it is
        // one log line showing a strafe that actually MOVES; the only one on
        // record was blocked by a wall. Until then the walk spends a few extra
        // turns and is never wrong.
        // ==================================================================

        /// <summary>
        /// Space 1 — the starting spot, with the game table to the north. The
        /// only place the game will let the player sit back down.
        /// </summary>
        public const int TABLE_SPACE_ID = 1;

        /// <summary>North, as a heading. The table is north of space 1.</summary>
        public const float TABLE_BEARING = 0f;

        // Spaces are ten units apart, so a neighbour is a space exactly one
        // grid step away on one axis and level on the other. Computed rather
        // than hand-listed: a typo in a hand-written adjacency table would
        // route the player into a space that does not exist, and the grid
        // itself is the thing that was measured.
        private const float GRID_STEP = 10f;
        private const float GRID_EPSILON = 0.5f;

        public static Space SpaceById(int id)
        {
            foreach (var s in SPACES) if (s.Id == id) return s;
            return null;
        }

        private static bool AreNeighbours(Space a, Space b)
        {
            if (a == null || b == null) return false;
            float dx = Mathf.Abs(a.X - b.X);
            float dz = Mathf.Abs(a.Z - b.Z);

            bool alongX = dz < GRID_EPSILON && Mathf.Abs(dx - GRID_STEP) < GRID_EPSILON;
            bool alongZ = dx < GRID_EPSILON && Mathf.Abs(dz - GRID_STEP) < GRID_EPSILON;
            return alongX || alongZ;
        }

        /// <summary>
        /// The world heading that carries the player from one space to the
        /// space next door, or null when they are not neighbours.
        /// </summary>
        public static float? BearingBetween(int fromId, int toId)
        {
            var a = SpaceById(fromId);
            var b = SpaceById(toId);
            if (!AreNeighbours(a, b)) return null;

            if (b.Z > a.Z + GRID_EPSILON) return 0f;    // north, +z
            if (b.Z < a.Z - GRID_EPSILON) return 180f;  // south, -z
            if (b.X > a.X + GRID_EPSILON) return 90f;   // east,  +x
            return 270f;                                // west,  -x
        }

        /// <summary>
        /// The shortest route between two spaces as a list of space ids,
        /// starting with <paramref name="fromId"/>. Null when there is no route.
        ///
        /// A BREADTH-FIRST SEARCH RATHER THAN "HEAD TOWARDS IT", because the
        /// grid has two holes in it. There is no space at (-0.75, 0) or
        /// (-0.75, 10), so from the shelf or the gramophone the table is NOT
        /// reachable by walking west and then south — that route leaves the
        /// room. Walking south first and then west is the only way, and a
        /// search finds that where a greedy step does not.
        ///
        /// Same method as the camera ladder's PathTo, one floor down, and for
        /// the same reason: routes are searched over data, never guessed.
        /// </summary>
        public static List<int> RouteBetween(int fromId, int toId)
        {
            if (SpaceById(fromId) == null || SpaceById(toId) == null) return null;
            if (fromId == toId) return new List<int> { fromId };

            var cameFrom = new Dictionary<int, int>();
            var seen     = new HashSet<int> { fromId };
            var queue    = new Queue<int>();
            queue.Enqueue(fromId);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (current == toId) break;

                foreach (var next in SPACES)
                {
                    if (seen.Contains(next.Id)) continue;
                    if (!AreNeighbours(SpaceById(current), next)) continue;

                    seen.Add(next.Id);
                    cameFrom[next.Id] = current;
                    queue.Enqueue(next.Id);
                }
            }

            if (!cameFrom.ContainsKey(toId)) return null;

            var route = new List<int>();
            int at = toId;
            while (at != fromId) { route.Add(at); at = cameFrom[at]; }
            route.Add(fromId);
            route.Reverse();
            return route;
        }

        // ==================================================================
        // THE DESCRIPTIONS.
        //
        // Key is "space|facing" — "3|north". Twenty-eight of them, and every one
        // is Zamar's to write. IKMA_Cabin_Descriptions.md in the project root is
        // the sheet; whatever he puts there goes here verbatim.
        //
        // EMPTY IS NOT A BUG. A space with nothing worth describing keeps the
        // fallback, because "Nothing is directly in front of you" is true and a
        // sighted player standing there learns nothing either.
        // ==================================================================
        private static readonly Dictionary<string, string> _descriptions
            = new Dictionary<string, string>
        {
            // ZAMAR'S TEXT, VERBATIM, from IKMA_Cabin_Descriptions.md.
            //
            // GENERATED FROM THE SHEET, NOT HAND-COPIED. (0.7.76.) He updated
            // the sheet in Session 17 and the table below did not move, so the
            // mod went on speaking the previous wording — three edits lost, and
            // the drift was invisible because both files looked right on their
            // own. Every rebuild of this block now re-parses the sheet, so the
            // only way they can disagree is if the compile step is skipped
            // entirely, which shows up as a stale timestamp on this file.
            //
            // NOT EDITED. Not shortened, not corrected, not "improved" — that is
            // the promise the sheet makes and the wording rule behind it. If a
            // line here reads oddly, it is his line and only he changes it.
            //
            // The remaining slots are deliberately empty. An empty slot keeps
            // "Nothing is directly in front of you", which is true: a sighted
            // player standing there learns nothing either.
            //
            // ***  SHIP BLOCKER: SEVERAL OF THESE LINES PROMISE KEYS THAT DO  ***
            // ***  NOTHING. 1, 2 and 3 are unbound while standing. Zamar       ***
            // ***  shipped the text knowingly; the interactions are owed       ***
            // ***  before release. See B8 and B14 in the handoff.              ***
            //
            // Do not "fix" this by editing his text. The keys get built.
            { "1|north", "Game Table. A knee high, heavy wooden table spans before you. A small wooden stool rests under you. Across the table, from the shadows, Leshy's eyes follow your every move. Up arrow to sit back down at the table." },
            { "1|south", "The dark wall of the cramped wooden cabin." },
            { "1|west", "Rule Book. On a shelf rests the rulebook. Above it bolted to the wall is a brush and hammer. A dim lantern rests on the shelf with a couple large mushrooms. Press R to open the rule book." },
            { "2|south", "Cuckoo Clock. On the wall in front of you hangs a massive Cuckoo Clock. It is not moving. There are three buttons on it. Press 1 to change the Hours hand. Press 2 to change the Minutes hand. Press 3 to change the Seconds hand." },
            { "3|east", "Large Safe and Woodcarving. On a shelf in front of you is a large safe with three dials, and a small woodcarving figure. The woodcarving is the game piece you use on the game map. Press 1 to view the Safe. Press 2 to view the woodcarving." },
            { "3|south", "Large Picture frame. Before you hangs a large frame with a photo that appears to be the game table, but with nothing on it." },
            { "4|west", "The Game Table, side view. Straight ahead sits the knee level game table. It is currently cleared off while Leshy waits for you to sit back down." },
            { "5|east", "Heavy Wooden Door. Before you stands a heavy wooden door. It has a large metal handle and a narrow eye level window. Periodically from behind the door flickers a bright light, and the sound of electric arcs. Press 1 to try the door handle." },
            { "6|north", "Ahead lies a shelf with a collection items on it, a globe, a human skull, and a woodcarving statue of a wolf that is locked inside a heavy metal cage. Press 1 to spin the globe. Press 2 to observe the skull closer. Press 3 to touch the caged wolf." },
            { "6|west", "You stand face to face with Leshy, a giant mass of darkness. He sits in a dark corner of the room, across the game table from where you have been told to sit. The only features you can make out are his eyes, and a small hint of a large white beard. He sits silently, observing every move you make within his cabin." },
            { "7|north", "Lock Box. Directly ahead sits a massive mahogany box. It is covered in mushrooms but looks well maintained. It has an ornate flower pattern on the front, and a large gold keyhole." },
            { "7|east", "Gramophone. On a mushroom covered shelf before you sits a Gramophone, with a record on it and another nearby. Press 1 to play a different music track." },
        };

        // ==================================================================
        // THE SECOND LEVEL — what 1, 2 and 3 say. (Session 17.)
        //
        // Key is "space|facing|digit" — "2|south|1". Generated from the same
        // sheet as the descriptions above and never hand-copied.
        //
        // WHY THE KEYS READ TEXT RATHER THAN ONLY DRIVING OBJECTS, and it is
        // Zamar's framing rather than a workaround:
        //
        //   "I know it's more of scenery than full puzzles in Kaycees mod, and
        //    that's the expectation I want us to set for the players... Getting
        //    them used to these controls now (and allowing us an early chance to
        //    build them out) is my goal with this KM standing functionality."
        //
        // So the cabin in Kaycee's Mod is a teaser for how the main game will
        // work. The control has to be REAL everywhere it is offered — that is
        // the never-offer-a-blind-player-a-dead-key rule — and a closer look at
        // the object is real whether or not the game has anything to click. In
        // the main game the same key will drive an actual puzzle.
        //
        // Where a live object DOES exist the key does both: read the closer
        // description and press the thing.
        //
        // AN EMPTY ENTRY IS NOT A BUG, same as the descriptions. It means he
        // has not written that one yet, and the key says so honestly.
        // ==================================================================
        private static readonly Dictionary<string, string> _examine
            = new Dictionary<string, string>
        {
            // Nothing authored yet.
        };

        /// <summary>
        /// The closer look for one number key at one slot, or null when nothing
        /// is authored for it.
        /// </summary>
        public static string ExamineFor(string slotKey, int digit)
        {
            if (string.IsNullOrEmpty(slotKey)) return null;
            string text;
            return _examine.TryGetValue($"{slotKey}|{digit}", out text) ? text : null;
        }

        // ==================================================================
        // THE GAME'S OWN NAMES FOR THESE SPACES. (Session 17.)
        //
        // FirstPersonController.CurrentZone returns a NavigationZone3D, and the
        // 0.7.77 survey printed their names: "North Middle", "North East",
        // "Middle East", "South East". They are a 3x3 of North/Middle/South by
        // West/Middle/East — the same seven spaces measured here, described the
        // same way, and the two the game never names are the two that are
        // absent from the grid.
        //
        // WHY THIS IS NOW THE LOOKUP OF RECORD. Coordinates fail exactly when
        // they are needed. Zooming into the safe put the camera at y=8.76, so
        // SpaceAt returned null, the number keys reported slot 'unknown' and
        // Backspace could never start a walk. The game knew where the player was
        // standing the whole time.
        //
        // NAMES ARE A LAST RESORT AND THIS PROJECT SAYS SO — a zone carries no
        // id or enum, only its GameObject name, so this is the only key there
        // is. An unrecognised name resolves to nothing and is LOGGED rather
        // than guessed at, which is what a new or renamed zone should do.
        // ==================================================================
        private static readonly Dictionary<string, int> _zoneToSpace
            = new Dictionary<string, int>
        {
            // "PLAYER SEAT", NOT "SOUTH WEST". Read off his 0.7.89 log rather
            // than inferred from the grid — the game names the starting space
            // after what it is FOR, and the guessed name meant space 1 resolved
            // to nothing, which is why R at the rulebook shelf had no slot.
            { "player seat",  1 },
            { "south west",   1 },
            { "south middle", 2 },
            { "south east",   3 },
            { "middle middle",4 },
            { "middle east",  5 },
            { "north middle", 6 },
            { "north east",   7 },
        };

        /// <summary>
        /// Which numbered space a zone is, or 0 when the name is not one this
        /// table knows. The caller logs a 0 — silently guessing would read out a
        /// description of somewhere the player is not.
        /// </summary>
        public static int SpaceIdForZone(string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName)) return 0;

            // A Unity object's ToString is "Name (TypeName)". Take the name.
            int paren = zoneName.IndexOf(" (");
            if (paren > 0) zoneName = zoneName.Substring(0, paren);

            int id;
            return _zoneToSpace.TryGetValue(zoneName.Trim().ToLowerInvariant(), out id) ? id : 0;
        }

        /// <summary>
        /// What Space says while zoomed in on the object a number key opened,
        /// or null when Zamar has written nothing for it. Key is the authored
        /// "space|facing|digit". (0.7.423.) A switch, not a table, so the text
        /// is read from Vocabulary at speak time and follows the language.
        /// </summary>
        public static string ZoomedLookFor(string slotDigitKey)
        {
            switch (slotDigitKey)
            {
                case "6|north|2": return Vocabulary.Cabin.SkullRestsOnShelf;
                default:          return null;
            }
        }

        /// <summary>The slot key for a space id and a facing, or null.</summary>
        public static string SlotKey(int spaceId, string facing)
        {
            if (spaceId <= 0 || string.IsNullOrEmpty(facing)) return null;
            return $"{spaceId}|{facing.ToLowerInvariant()}";
        }

        // ==================================================================
        // NAMES FOR THE ZOOMABLE OBJECTS.
        //
        // A zoom object has NO display name anywhere in the assembly. 'Safe' is
        // its GameObject name, and speaking GameObject names is precisely the
        // bug that was fixed across the whole Kaycee's Mod front end — `via
        // gameObject name` in a log has always been treated as a defect.
        //
        // So every word here is one Zamar has already written, lifted from his
        // own slot descriptions rather than invented:
        //     "Large Safe. On a shelf in front of you..."          -> Safe
        //     "Large Picture frame. Before you hangs a large..."   -> Picture frame
        //
        // ANYTHING NOT IN THIS TABLE GETS NO NAME. The caller logs it and says
        // the short form instead, so a missing word shows up as a request to
        // him rather than as a wrong word in his mod.
        // ==================================================================
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _zoomNames => Loc.PerLanguage(ref _zoomNamesCache, ref _zoomNamesLanguage, Build_zoomNames);
        private static Dictionary<string, string> Build_zoomNames() =>
            new Dictionary<string, string>
        {
            { "safe", Vocabulary.Cabin.Safe },

            // The three-part woodcarving on the shelf beside the safe. Its
            // GameObject name is PastFigurines, which says nothing to a player —
            // Zamar: "Should be 'you focus on the woodcarving'."
            { "pastfigurines", Vocabulary.Cabin.Woodcarving },

            // Added 0.7.112. Unlike the skull, both of these GameObject names
            // occur exactly once in his sweep, so keying on the name is safe
            // here and covers the case a number key cannot: a zoom the player
            // opened with the MOUSE, where there is no digit to look up.
            { "largepictureframe",     Vocabulary.Cabin.LargePictureFrame },
            { "gramophoneinteractable", Vocabulary.Cabin.Gramophone         },
        };
        private static Dictionary<string, string> _zoomNamesCache;
        private static string _zoomNamesLanguage;

        // ==================================================================
        // ZOOM NAMES KEYED BY THE AUTHORED SLOT AND DIGIT. (0.7.111.)
        //
        // THE GAMEOBJECT NAME IS NOT UNIQUE, AND THAT IS NOT A DETAIL.
        // His 0.7.110 sweep found TEN separate objects whose GameObject name is
        // the bare string "ZoomInteractable" — the skull at the shelf, eight on
        // the east wall around x=30, and one more beside the safe. The table
        // above is keyed on that name, so one entry for "zoominteractable" would
        // have named all ten the same thing and nine of them wrongly.
        //
        // This is the project's oldest rule arriving from a new direction:
        // internal ids are not display names, and here the id is not even an
        // id — it is a type name left on a prefab ten times over.
        //
        // SO THE KEY IS WHAT ZAMAR AUTHORED. "6|north|2" is his own sentence
        // "Press 2 to observe the skull closer", and there is exactly one of
        // those. The digit that opened a zoom is a fact IKMA owns; the
        // GameObject on the other end of it is the game's, and it turned out not
        // to be an identity at all.
        //
        // The name table above stays as the fallback for objects whose
        // GameObject name IS unique (Safe, PastFigurines), and for a zoom the
        // player opened with the mouse, where no digit was pressed.
        //
        // EVERY WORD IN HERE IS HIS. Session 18: "Change to you focus in on the
        // skull. You step back from the skull."
        // ==================================================================
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _zoomNamesByDigit => Loc.PerLanguage(ref _zoomNamesByDigitCache, ref _zoomNamesByDigitLanguage, Build_zoomNamesByDigit);
        private static Dictionary<string, string> Build_zoomNamesByDigit() =>
            new Dictionary<string, string>
        {
            { "6|north|2", Vocabulary.Cabin.Skull },

            // Session 18, his words: "Just make it 'You focus on the Large
            // Picture Frame.' 'You step back from the Large Picture Frame.' and
            // 'You focus on the Gramophone.'"
            { "3|south|1", Vocabulary.Cabin.LargePictureFrame },
            { "7|east|1",  Vocabulary.Cabin.Gramophone },
        };
        private static Dictionary<string, string> _zoomNamesByDigitCache;
        private static string _zoomNamesByDigitLanguage;

        // ==================================================================
        // THE WOODCARVING'S PIECES. (0.7.112.)
        //
        // THESE ARE THE GAME'S OWN NAMES, not invented ones. FigurineType is a
        // nested enum on CompositeFigurine and its eight members are
        // SettlerMan, SettlerWoman, Chief, Wildling, Prospector, Enchantress,
        // Gravedigger and Robot. A ninth member, NUM_FIGURINES, is a count
        // sentinel and is not a piece — reading it as one would announce a
        // figurine that does not exist.
        //
        // THE USUAL RULE SAYS INTERNAL IDS ARE NOT DISPLAY NAMES, and it was put
        // to Zamar rather than assumed either way. There is no display-name
        // table for FigurineType anywhere in the assembly — the game shows a
        // carved shape and never a word — so the enum is the only source there
        // is. His call, Session 18: "I'll confirm just put those names in for
        // now."
        //
        // SIX OF THE EIGHT ARE ALREADY WORDS. The two that are not are spelled
        // out here rather than run through a CamelCase splitter, so each spoken
        // name is one string he can change without touching any code.
        // ==================================================================
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _figurineNames => Loc.PerLanguage(ref _figurineNamesCache, ref _figurineNamesLanguage, Build_figurineNames);
        private static Dictionary<string, string> Build_figurineNames() =>
            new Dictionary<string, string>
        {
            { "settlerman",   Vocabulary.Cabin.SettlerMan   },
            { "settlerwoman", Vocabulary.Cabin.SettlerWoman },
            { "chief",        Vocabulary.Cabin.Chief         },
            { "wildling",     Vocabulary.Cabin.Wildling      },
            { "prospector",   Vocabulary.Cabin.Prospector    },
            { "enchantress",  Vocabulary.Cabin.Enchantress   },
            { "gravedigger",  Vocabulary.Cabin.Gravedigger   },
            { "robot",        Vocabulary.Cabin.Robot         },
        };
        private static Dictionary<string, string> _figurineNamesCache;
        private static string _figurineNamesLanguage;

        // ==================================================================
        // THE THREE PARTS, IN HIS WORDS.
        //
        // Zamar's format, Session 18: "have it say 'Chief head.' 'Robot legs.'
        // etc. format should be [type] [bodypart] for quick identification."
        //
        // "BODY", NOT "LEGS" — AND THIS SETTLED A GENERAL RULE.
        //
        // His example said "Robot legs." The three controls are
        // HeadInteractable, ArmsInteractable and BodyInteractable, and
        // CompositeFigurine stores definedHead / definedArms / definedBody, so
        // the game calls that part the body. Flagged, and he changed it:
        //
        //   "make it body yes. If the game has actual descriptions default to
        //    those over mine unless said otherwise. Something like this is a
        //    subjective visual, but if the files say it's body and not legs
        //    then that's going to be more accurate."
        //
        // THE RULE, IN FULL, BECAUSE IT MODIFIES AN OLDER ONE. Where the game
        // has its own word for a thing, that word wins over a description he
        // wrote from looking at it — his prose is authored against a visual and
        // a visual can be read wrong, as the wand that is a Brush and the clock
        // hands that were the other way round both showed in one session.
        //
        // THIS DOES NOT LOOSEN THE WORDING RULE. Where the game has NO word,
        // every syllable is still his and still has to be asked for. The change
        // is only about which source wins when both have one, and it points the
        // same direction the project already went with rulebookName,
        // GetTranslatedStatText and AscensionChallengeInfo.title: find the data
        // object, do not invent a name.
        // ==================================================================
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _figurineParts => Loc.PerLanguage(ref _figurinePartsCache, ref _figurinePartsLanguage, Build_figurineParts);
        private static Dictionary<string, string> Build_figurineParts() =>
            new Dictionary<string, string>
        {
            { "head", Vocabulary.Cabin.Head },
            { "arms", Vocabulary.Cabin.Arms },
            { "body", Vocabulary.Cabin.Body },
        };
        private static Dictionary<string, string> _figurinePartsCache;
        private static string _figurinePartsLanguage;

        /// <summary>
        /// "Chief head." for one piece, or null when the type is unknown — an
        /// unknown enum member is logged and dropped rather than spoken, the
        /// same way an unconfirmed rulebook token is.
        /// </summary>
        public static string FigurinePiece(string enumName, string partKey)
        {
            if (string.IsNullOrEmpty(enumName) || string.IsNullOrEmpty(partKey)) return null;

            string type, part;
            if (!_figurineNames.TryGetValue(enumName.Trim().ToLowerInvariant(), out type)) return null;
            if (!_figurineParts.TryGetValue(partKey.Trim().ToLowerInvariant(), out part)) return null;

            return $"{type} {part}.";
        }

        /// <summary>
        /// The approved word for the zoom a given number key opens, or null.
        /// Keyed on the authored slot and digit because GameObject names on
        /// these objects are not unique — see the comment above.
        /// </summary>
        public static string ZoomDisplayNameForDigit(string slotKey, int digit)
        {
            if (string.IsNullOrEmpty(slotKey)) return null;
            string spoken;
            return _zoomNamesByDigit.TryGetValue($"{slotKey}|{digit}", out spoken)
                 ? spoken : null;
        }

        // ==================================================================
        // WHICH OBJECT EACH NUMBER KEY PRESSES. (Session 17.)
        //
        // The registry can say WHICH objects are usable from where the player
        // stands, but not which of them Zamar's text means by "1". At the safe
        // his description reads "Press 1 to view the Safe. Press 2 to view the
        // woodcarving." and the two objects that match that slot came back in
        // the log as HoldableFigurineShelf and Safe — his order and the game's
        // order are not the same, and nothing in the data relates them.
        //
        // So the mapping is explicit, and it is seeded ONLY with pairings a log
        // has actually confirmed. Key is "space|facing|digit", value is the
        // object's GameObject name.
        //
        // A digit with no entry falls back to the Nth match in stable order and
        // LOGS what it pressed, so the next run names the object and the entry
        // gets added. That fallback is deliberately noisy: a number key opening
        // the wrong thing is exactly the kind of quiet wrongness this project
        // treats as a bug.
        // ==================================================================
        private static readonly Dictionary<string, string> _digitToObject
            = new Dictionary<string, string>
        {
            // CONFIRMED PAIRINGS, each read off a real match list in his logs.
            { "3|east|1", "Safe" },
            { "3|east|2", "PastFigurines" },          // the woodcarving figure

            // THE RULEBOOK SHELF'S WALL ITEMS. (Session 18, his ask: "1 should be
            // touch the wand, 2 should be touch the hammer." -> "brush", below.
            //
            // BOTH NAMES COME OFF HIS 0.7.110 SWEEP, and both carry zone data —
            // zone 'Player Seat', direction West, which is exactly slot 1 facing
            // west. So they arrive through the normal zone match; no loose
            // lookup is involved.
            //
            // HIS WORD WAS "WAND" AND HE CHANGED IT TO "BRUSH". Session 18,
            // after being shown the GameObject name: "it looks like a wand, but
            // my bad! update everything to say/read Brush instead of wand." So
            // his sheet, the compiled description and these comments all say
            // brush now, and they agree with WallFastenedObject_Brush.
            //
            // THE FLAG WAS WORTH RAISING. A mismatch between his prose and the
            // object IKMA presses is invisible in both files on their own — the
            // same shape as the cabin descriptions drifting out of sync. Naming
            // it cost one sentence and corrected the mod's vocabulary.
            //
            // SAFE TO PRESS, CHECKED FIRST. FastenedObjectInteractable declares
            // exactly two fields — objectTransform and soundId — and one method,
            // its own OnCursorSelectStart override. No pickup, no collect, no
            // state. That is the same shape as the caged wolf: scenery that
            // moves a little and plays a sound. The rule from the wolf still
            // applies and this one clears it.
            { "1|west|1", "WallFastenedObject_Brush"  },   // his "brush"
            { "1|west|2", "WallFastenedObject_Hammer" },

            // Zamar: "Interacting with it should be 1 though not 2, you cannot
            // change the music without focusing in on it." The fallback was
            // handing 1 to BarrelShelf because it sorts first.
            { "7|east|1", "GramophoneInteractable" },

            // The shelf: 1 spins the globe, 2 inspects the skull. His order,
            // which is not the order the objects happen to register in.
            { "6|north|1", "GlobeInteractable" },
            { "6|north|2", "ZoomInteractable" },      // the skull's zoom

            // THE CAGED WOLF, and it is a LOOSE object — no zone data, so the
            // zone match never offered it and key 3 fell through to a
            // CandleFlame three presses running. Zamar: "3 should mouse click on
            // the cage to make a sound, which it currently doesnt."
            //
            // THREE WRONG ANSWERS BEFORE THIS ONE, AND HIS MOUSE SETTLED IT.
            // 0.7.106 and 0.7.107 both mapped this digit to
            // DiscoverableCard_CagedWolf, because that is what the 0.7.105 sweep
            // named near the shelf. Pressing it handed him a Caged Wolf card the
            // game had never offered — collecting, not touching. 0.7.109 made the
            // digit hover-only, which was safe and still not the interaction.
            //
            // NONE OF THOSE WAS THE OBJECT A MOUSE LANDS ON. Session 18: he
            // hovered and clicked the cage himself, and the CursorEnter patch
            // named what the cursor actually entered —
            //
            //     HoldableWolfStatue obj='GrabbableWolfStatue'
            //     x=9.52 y=7.17 z=18.29   Enabled=True
            //
            // against DiscoverableCard_CagedWolf at x=17.84, Enabled=False. A
            // different object, eight units down the shelf, and disabled in
            // Kaycee's Mod. GrabbableWolfStatue sits at exactly the coordinates
            // of WolfStatueShelf, which is the shelf slot his description names.
            //
            // ASK THE PLAYER WHAT THE GAME DOES — it is faster than a third
            // guess, and two guesses here were already wrong, one of them
            // expensively. His answer for what the click does: "the click just
            // shook the cage and make a quick sound effect. That's all I'm
            // looking to happen." That is HoldableWolfStatue.Jitter(), which the
            // type declares beside JITTER_DURATION and its own
            // OnCursorSelectStart override.
            //
            // SO THIS PRESSES, IT DOES NOT HOVER. The statue is caged, and the
            // game's own override is what decides that a caged statue jitters
            // rather than being picked up. IKMA presses the same primitive a
            // mouse click does and lets the game answer; it never reproduces the
            // shake or the sound. The object is in the loose registry from the
            // cabin sweep (`[loose] HoldableWolfStatue`, 0.7.109 log line 329),
            // so no mouse is needed to discover it.
            //
            // The digit stays SILENT: the sound effect IS the feedback, and no
            // word has been approved for this one.
            { "6|north|3", "GrabbableWolfStatue" },

            // THE CUCKOO CLOCK'S THREE HANDS. Taken from his 0.7.105 log, where
            // the unmapped fallback happened to press them in exactly this order
            // and he reported the right hand turning each time.
            //
            // NOT A ZOOM. Zamar: "There is no focusing in on the cucoo clock, it
            // should just say which hand you adjusted with 1/2/3." A knob turns
            // a hand in place, so "You focus in" describes something that does
            // not happen — the call he already made for the globe and the door.
            { "2|south|1", "ClockKnobInteractable_Hour"   },
            { "2|south|2", "ClockKnobInteractable_Minute" },
            { "2|south|3", "ClockKnobInteractable_Second" },

            // Zamar: "On the door, 2 is working as expected but it should be on
            // 1." The fallback was handing 1 to whichever object sorted first.
            { "5|east|1", "DoorKnobInteractable" },
        };

        // ==================================================================
        // SLOTS WHERE THE NUMBER KEYS ARE DELIBERATELY DEAD.
        //
        // Zamar: "the 1/2/3 keys for the Mahogony box should be disabled since
        // that puzzle is not accessible in Kaycees mod."
        //
        // The wardrobe drawers DO register and DO respond, so without this the
        // fallback would happily open a drawer belonging to a puzzle that does
        // not exist in this mode. A key that does something meaningless is worse
        // than one that does nothing, because the player builds a model around
        // it — and his description of that space promises nothing.
        // ==================================================================
        private static readonly HashSet<string> _noNumberKeys
            = new HashSet<string>
        {
            // The mahogany box. Zamar: "that puzzle is not accessible in
            // Kaycees mod." The drawers behind it DO respond, so without this
            // the fallback opens something meaningless.
            "7|north",

            // THE RULEBOOK SHELF, AND THIS ONE WAS A SOFTLOCK. (0.7.91.)
            // Pressing 1 there hit CabinRulebookInteractable, which opens the
            // book by a path IKMA's rulebook layer never sees — so the book was
            // up and no reader owned the keyboard. He had to escape with the
            // mouse.
            //
            // He wants 1 and 2 on the two wall items and 3 doing nothing. Their
            // GameObject names have never appeared in a log, so the keys are
            // OFF here rather than guessing at which object is which — a wrong
            // guess here is another softlock. R still opens the book, which is
            // the route that works and the one his description names.
            //
            // RESOLVED AT 0.7.111 AND THE SLOT IS BACK ON. His 0.7.110 sweep
            // named both wall items — WallFastenedObject_Brush and
            // WallFastenedObject_Hammer — so 1 and 2 are now MAPPED by name
            // rather than taken from the match order. The softlock came from
            // the unmapped fallback reaching CabinRulebookInteractable, and a
            // mapped digit never consults that order at all.
            //
            // 3 IS OFF EXPLICITLY, in _disabledDigits below. That is the guard
            // that used to be this whole line: with the slot switched back on,
            // an unmapped 3 would fall through to the Nth match and the book is
            // still sitting in that list.
        };

        // ==================================================================
        // SLOTS WHERE PRESSING A KEY SHOULD NOT ANNOUNCE ITSELF.
        //
        // Zamar, 0.7.91: "on the globe table, 1 and 3 should not read you focus
        // in. No reading needed for those." and on the door: "the You focus in
        // line should not be on this screen."
        //
        // The line earns its place when the camera actually moves in on
        // something — the safe, the picture frame. Spinning a globe or trying a
        // handle does not move you anywhere, so announcing that you focused is
        // describing something that did not happen. The game's own sound is the
        // feedback.
        // ==================================================================
        private static readonly HashSet<string> _silentDigits
            = new HashSet<string>
        {
            "6|north|1",   // spin the globe
            "5|east|1",    // try the door handle

            // The caged wolf. Silent on purpose: the game's own shake and sound
            // effect ARE the feedback, and Zamar has approved no word for it.
            "6|north|3",

            // The brush and the hammer. Silent for the wolf's reason: a
            // FastenedObjectInteractable carries a soundId and its own select
            // handler, so the game's knock and rattle ARE the feedback, and
            // "You focus in" would describe a camera move that does not happen.
            "1|west|1", "1|west|2",

            // The clock knobs say their own hand and time instead — ClockHandName.
            "2|south|1", "2|south|2", "2|south|3",
        };

        // ==================================================================
        // THE CLOCK'S HANDS, named by the knob that turns them.
        //
        // His format: "[Hand] to [x o'clock]". The NUMBER is read off the
        // clock's own handPositions after the press and never predicted — and
        // solutionPositionsLarge and solutionPositionsSmall, which sit on that
        // same object, are the ANSWER to the puzzle and are never read. Same
        // rule the safe's correctLockPositions has, and it would be trivial to
        // break by accident from here.
        // ==================================================================
        // Session 32: rebuilt when the spoken language changes (Loc.PerLanguage),
        // because it holds Vocabulary text, which is translated.
        private static Dictionary<string, string> _clockHands => Loc.PerLanguage(ref _clockHandsCache, ref _clockHandsLanguage, Build_clockHands);
        private static Dictionary<string, string> Build_clockHands() =>
            new Dictionary<string, string>
        {
            { "clockknobinteractable_hour",   Vocabulary.Cabin.HourHand   },
            { "clockknobinteractable_minute", Vocabulary.Cabin.MinuteHand },
            { "clockknobinteractable_second", Vocabulary.Cabin.SecondHand },
        };
        private static Dictionary<string, string> _clockHandsCache;
        private static string _clockHandsLanguage;

        public static string ClockHandName(string gameObjectName)
        {
            if (string.IsNullOrEmpty(gameObjectName)) return null;
            string spoken;
            return _clockHands.TryGetValue(gameObjectName.Trim().ToLowerInvariant(), out spoken)
                 ? spoken : null;
        }

        public static bool DigitIsSilent(string slotKey, int digit)
        {
            return !string.IsNullOrEmpty(slotKey)
                && _silentDigits.Contains($"{slotKey}|{digit}");
        }

        // ==================================================================
        // SINGLE KEYS THAT DO NOTHING, where the rest of the slot still works.
        //
        // Zamar, 0.7.104: "2/3 should not do anything on the door screen when
        // standing up. Only 1 to try the handle, which works as expected."
        //
        // Different from _noNumberKeys, which switches a whole slot off. Here
        // the slot has exactly one thing in it, and the fallback — press
        // whatever else is registered in this zone — would find some neighbour
        // and act on it. Offering a key that does something unrelated is worse
        // than offering one that does nothing, and both are worse than saying so.
        // ==================================================================
        private static readonly HashSet<string> _disabledDigits
            = new HashSet<string>
        {
            "5|east|2",   // the door has one handle and nothing else
            "5|east|3",

            // THE RULEBOOK SHELF'S THIRD KEY, AND IT IS LOAD BEARING. (0.7.111.)
            //
            // 1 and 2 are the brush and the hammer. His description promises
            // nothing for 3, and the slot's match list still contains
            // CabinRulebookInteractable — pressing that opens the book by a path
            // IKMA's rulebook layer never sees, which left the book up with no
            // reader owning the keyboard and cost him a mouse rescue at 0.7.91.
            //
            // The whole slot used to be switched off to prevent exactly that.
            // Turning it back on for the two mapped digits means this one has to
            // carry the guard on its own.
            "1|west|3",
        };

        // ==================================================================
        // KEYS THAT TOUCH RATHER THAN PRESS. (0.7.109.)
        //
        // PRESSING THE CAGED WOLF TOOK THE CARD. Zamar: "I pulled a Caged Wolf
        // card out of nowhere and Leshy said Curious... That shouldnt have
        // happened probably unless I had that card in my deck. I just wanted to
        // touch the caged wolf and hear the sound effect for it."
        //
        // He is right and this is the worst kind of bug this project can have.
        // DiscoverableCard_CagedWolf is a DiscoverableCardInteractable, and
        // selecting one is how you COLLECT it — so the accessibility layer
        // handed him a card the game had not offered. Parity in both directions
        // means never withholding what a sighted player can reach AND never
        // granting what they cannot; this broke the second half.
        //
        // So this digit hovered and let go. CursorEnter and CursorExit run the
        // object's own hover response — the highlight and whatever sound it
        // makes — and cannot change game state, because hovering never does.
        //
        // THE RULE THIS WROTE DOWN, AND IT STANDS: a number key presses
        // SCENERY. Anything that can be picked up, collected or spent is
        // touched, or left alone. When in doubt about which an object is, it is
        // the second one.
        //
        // THE SET IS EMPTY AGAIN AT 0.7.110, AND THE REASON MATTERS.
        // Hover-only was the right answer to the wrong object. The digit was
        // pointed at DiscoverableCard_CagedWolf — a card pickup — where the
        // rule above says do not press. Session 18 found that a mouse click on
        // the cage lands on GrabbableWolfStatue instead, and Zamar confirmed
        // what that click does: the cage shakes and a sound plays, which is the
        // whole interaction he wants. So the digit presses the correct object
        // rather than half-pressing the wrong one.
        //
        // KEEP THIS MACHINERY. The next object that turns out to be collectable
        // goes in this set, and the alternative — dropping the digit entirely —
        // would leave one of his authored descriptions promising a dead key.
        // ==================================================================
        private static readonly HashSet<string> _touchOnly
            = new HashSet<string>();

        public static bool DigitIsTouchOnly(string slotKey, int digit)
        {
            return !string.IsNullOrEmpty(slotKey)
                && _touchOnly.Contains($"{slotKey}|{digit}");
        }

        public static bool DigitDisabled(string slotKey, int digit)
        {
            return !string.IsNullOrEmpty(slotKey)
                && _disabledDigits.Contains($"{slotKey}|{digit}");
        }

        public static bool NumberKeysDisabled(string slotKey)
        {
            return !string.IsNullOrEmpty(slotKey) && _noNumberKeys.Contains(slotKey);
        }

        /// <summary>The GameObject name a number key should press, or null.</summary>
        public static string ObjectForDigit(string slotKey, int digit)
        {
            if (string.IsNullOrEmpty(slotKey)) return null;
            string name;
            return _digitToObject.TryGetValue($"{slotKey}|{digit}", out name) ? name : null;
        }

        /// <summary>An approved spoken name for a zoom object, or null.</summary>
        public static string ZoomDisplayName(string gameObjectName)
        {
            if (string.IsNullOrEmpty(gameObjectName)) return null;
            string spoken;
            return _zoomNames.TryGetValue(gameObjectName.Trim().ToLowerInvariant(), out spoken)
                 ? spoken : null;
        }

        /// <summary>The authored description for a slot key, or null.</summary>
        public static string DescriptionFor(string slotKey)
        {
            if (string.IsNullOrEmpty(slotKey)) return null;
            string text;
            return _descriptions.TryGetValue(slotKey, out text) ? text : null;
        }

        /// <summary>
        /// What is in front of the player, or null when nothing is authored for
        /// where they are standing.
        /// </summary>
        public static string DescribeAhead(Vector3 position, float heading, out string surveyKey)
        {
            surveyKey = null;

            var space = SpaceAt(position);
            string facing = FacingAt(heading);

            if (space == null || facing == null) return null;

            surveyKey = $"{space.Id}|{facing}";

            string described;
            return _descriptions.TryGetValue(surveyKey, out described) ? described : null;
        }
    }
}
