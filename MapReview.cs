// MapReview.cs
using System.Collections.Generic;
using DiskCardGame;

namespace IKMA
{
    /// <summary>
    /// THE MAP PREVIEW. (0.4.8.007, Session 58.)
    ///
    /// The tester: "it feels extremely weird to preview paths as a long list of
    /// nodes. If you've ever played MT2 or STS2, I think their presentation of
    /// the map is great." Zamar: conform. This is Say the Spire 2's
    /// TreeMapViewer with its default settings (no auto-advance), ported:
    ///
    ///   Up / Down (no Ctrl)  pick between the paths right ahead, as before.
    ///   Ctrl+Up      one step further along the path, to its leftmost
    ///                next node; "choice, " first when that row splits.
    ///   Ctrl+Down    one step back the way the preview came.
    ///   Ctrl+Left / Ctrl+Right   the other nodes of the same split; at either
    ///                end nothing is said (theirs).
    ///   Ctrl+Space   route summary from the node the preview is on: for each
    ///                kind of node, the fewest and most of it any single route
    ///                holds ("Campfire 0 to 2, Trader 1").
    ///
    /// The preview starts on the path the arrows are on (MapReader.BrowseChoices
    /// calls StartAt) and resets on arrival. It never moves the game's hover:
    /// the screen keeps showing the path the player has picked.
    ///
    /// FOUR ROWS. Zamar's parity rule (Session 16): a sighted player sees four
    /// rows ahead, so the preview stops there - "Path continues out of sight."
    /// (his words) - and the summary counts only those rows.
    /// </summary>
    internal static class MapReview
    {
        private static MapNode _current;
        // 0.4.8.010 - THE -1 DEFAULT. Zamar, his 2026-10-10 playtest: the
        // first Ctrl+Up "skipped the encounter that was next. I think it
        // needs that -1 by default thing so the first option is the first
        // heard one." StartAt lands on the selected path without saying it,
        // so the first Ctrl+Up reads that node instead of moving past it.
        private static bool _unheard;
        // The nodes the preview came through; [0] is where the player stands.
        private static readonly List<MapNode> _path = new List<MapNode>();

        internal static void Reset()
        {
            _current = null;
            _path.Clear();
            _unheard = false;
        }

        internal static void StartAt(MapNode choice)
        {
            Reset();
            var here = MapNodeManager.Instance?.ActiveNode;
            if (here == null || choice == null) return;
            _path.Add(here);
            _current = choice;
            _unheard = true;
        }

        private static bool Ensure()
        {
            var active = MapNodeManager.Instance?.ActiveNode;
            if (_current != null && _path.Count > 0 && ReferenceEquals(_path[0], active))
                return true;
            // 0.4.8.010 - back on the node the player stands on is a place in
            // the preview too. Before, Ensure started over from the selected
            // path here, so Ctrl+Down repeated "Woodcarver." instead of "No path
            // backward." and the next Ctrl+Up skipped row 1 (his 2026-10-10 log).
            if (_current != null && _path.Count == 0 && ReferenceEquals(_current, active))
                return true;
            StartAt(MapReader.GetSelectedChoice());
            return _current != null;
        }

        private static List<MapNode> Children(MapNode node)
        {
            var result = new List<MapNode>();
            var next = node?.connectedNodes;
            if (next == null) return result;
            foreach (var n in next) if (n != null) result.Add(n);
            result.Sort((a, b) => MapReader.NodeX(a).CompareTo(MapReader.NodeX(b)));
            return result;
        }

        // The nodes the current one shares a split with, left to right.
        private static List<MapNode> Siblings()
        {
            if (_path.Count == 0) return new List<MapNode> { _current };
            return Children(_path[_path.Count - 1]);
        }

        private static string Name(MapNode node) => MapReader.GetNodeFriendlyName(node);

        private static string Described(bool choicePrefix)
        {
            string name = Name(_current);
            if (choicePrefix && Siblings().Count > 1)
                return Vocabulary.BufferWords.Choice + ", " + name + ".";
            return name + ".";
        }

        internal static bool HandleKeys(bool up, bool down, bool left, bool right, bool space)
        {
            try
            {
                if (!Ensure())
                {
                    Speech.Browse(Vocabulary.NoPathsAhead);
                    return true;
                }
                if (up && _unheard)
                {
                    _unheard = false;
                    Plugin.Log?.LogInfo($"IKMA MAP PREVIEW: first look, {Name(_current)}, row {_path.Count}.");
                    Speech.Browse(Described(true));
                    return true;
                }
                if (!space) _unheard = false;
                if (up)         Forward();
                else if (down)  Backward();
                else if (left)  Branch(-1);
                else if (right) Branch(1);
                else if (space) Summary();
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"IKMA MAP PREVIEW: {e.GetType().Name}: {e.Message}");
            }
            return true;
        }

        private static void Forward()
        {
            var kids = Children(_current);
            if (kids.Count == 0)
            {
                // 0.4.8.010 - the game spawns only the map nodes in view
                // (MapDataReader: ElementWithinRange); a path that runs off the
                // paper is drawn to the edge (DrawPathToOffscreenNode) but the
                // node past it has no MapNode, so connectedNodes is empty. Ask
                // the node's own data whether the path goes on.
                bool continues = false;
                try { continues = _current?.Data?.connectedNodes != null && _current.Data.connectedNodes.Count > 0; } catch { }
                Speech.Browse(continues ? Vocabulary.BufferWords.PathOutOfSight : Vocabulary.BufferWords.NoPathForward);
                return;
            }
            // _path.Count is the current node's row: 1 = the paths right ahead.
            if (_path.Count >= MapReader.PATH_DEPTH)
            {
                Speech.Browse(Vocabulary.BufferWords.PathOutOfSight);
                return;
            }
            _path.Add(_current);
            _current = kids[0];
            Plugin.Log?.LogInfo($"IKMA MAP PREVIEW: forward to {Name(_current)}, row {_path.Count}.");
            Speech.Browse(Described(true));
        }

        private static void Backward()
        {
            if (_path.Count == 0) { Speech.Browse(Vocabulary.BufferWords.NoPathBackward); return; }
            _current = _path[_path.Count - 1];
            _path.RemoveAt(_path.Count - 1);
            Plugin.Log?.LogInfo($"IKMA MAP PREVIEW: back to {Name(_current)}, row {_path.Count}.");
            Speech.Browse(Described(true));
        }

        private static void Branch(int direction)
        {
            if (_path.Count == 0) return;                 // where the player stands: no split
            var sibs = Siblings();
            int i = sibs.IndexOf(_current);
            if (i < 0 || !ListStep.Step(ref i, direction, sibs.Count)) return;   // theirs: silent at the ends
            _current = sibs[i];
            Speech.Browse(Described(false));
        }

        // Theirs: ROUTES.TYPE_RANGE / TYPE_EXACT, counting the focused node.
        private static void Summary()
        {
            var starts = _path.Count == 0 ? Children(_current) : new List<MapNode> { _current };
            int startRow = _path.Count == 0 ? 1 : _path.Count;

            var routes = new List<Dictionary<string, int>>();
            var order = new List<string>();
            foreach (var s in starts)
                Walk(s, startRow, new Dictionary<string, int>(), routes, order);

            if (routes.Count == 0) { Speech.Browse(Vocabulary.NoPathsAhead); return; }

            var parts = new List<string>();
            foreach (string type in order)
            {
                int min = int.MaxValue, max = 0;
                foreach (var r in routes)
                {
                    int c;
                    r.TryGetValue(type, out c);
                    if (c < min) min = c;
                    if (c > max) max = c;
                }
                parts.Add(min == max
                    ? Vocabulary.BufferWords.TypeExact(type, max)
                    : Vocabulary.BufferWords.TypeRange(type, min, max));
            }
            Plugin.Log?.LogInfo($"IKMA MAP PREVIEW: summary over {routes.Count} route(s).");
            Speech.Browse(string.Join(", ", parts.ToArray()) + ".");
        }

        private const int MAX_ROUTES = 200;

        private static void Walk(MapNode node, int row, Dictionary<string, int> counts,
                                 List<Dictionary<string, int>> routes, List<string> order)
        {
            if (node == null || routes.Count >= MAX_ROUTES) return;
            string type = Name(node);
            if (!order.Contains(type)) order.Add(type);
            var mine = new Dictionary<string, int>(counts);
            int c;
            mine.TryGetValue(type, out c);
            mine[type] = c + 1;

            var kids = row >= MapReader.PATH_DEPTH ? new List<MapNode>() : Children(node);
            if (kids.Count == 0) { routes.Add(mine); return; }
            foreach (var k in kids) Walk(k, row + 1, mine, routes, order);
        }
    }
}
// MapReview.cs
