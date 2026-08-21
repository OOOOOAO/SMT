namespace SMT.EVEData
{
    /// <summary>
    /// Thin adapter that wires the real game data (<see cref="EveManager"/>) into the data-only
    /// <see cref="BookmarkParser"/> / <see cref="BookmarkRouteSolver"/>. Safe to call from a background
    /// thread : it only reads already-loaded EveManager state.
    /// </summary>
    public static class BookmarkRouteAdapter
    {
        /// <summary>
        /// Parses the pasted bookmark text and plans a full route from <paramref name="startSystem"/>.
        /// </summary>
        public static BookmarkRoute PlanRoute(
            string bookmarkText, string startSystem, int k, int jumpCost, decimal maxLY, int isolationJumps, int isolationKeepBookmarks, bool avoidHighSec, IEnumerable<string> avoidSystems)
        {
            ParseResult parsed = BookmarkParser.Parse(bookmarkText, ResolveSystemName);

            HashSet<string> avoid = avoidSystems != null
                ? new HashSet<string>(avoidSystems, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, List<string>> adjacency = BuildAdjacency(avoidHighSec, avoid);

            return BookmarkRouteSolver.Solve(
                adjacency,
                (a, b) => EveManager.Instance.GetRangeBetweenSystems(a, b),
                startSystem,
                parsed.BookmarkCounts.Keys,
                parsed.BookmarkCounts,
                parsed.UnparsedLines,
                k,
                jumpCost,
                maxLY,
                isolationJumps,
                isolationKeepBookmarks);
        }

        // Exact whole-field match only, English names, OrdinalIgnoreCase (GetEveSystem already does this) : see D3.
        private static string ResolveSystemName(string s)
        {
            return EveManager.Instance.GetEveSystem(s)?.Name;
        }

        // Builds the gate graph with excluded systems (highsec / avoid list) dropped entirely, as both a
        // key and a neighbour, so the BFS can never route through or land on one.
        private static Dictionary<string, List<string>> BuildAdjacency(bool avoidHighSec, HashSet<string> avoid)
        {
            Dictionary<string, List<string>> adjacency = new Dictionary<string, List<string>>();

            foreach (System sys in EveManager.Instance.Systems)
            {
                if (IsExcluded(sys, avoidHighSec, avoid))
                {
                    continue;
                }

                List<string> neighbours = new List<string>();
                foreach (string jump in sys.Jumps)
                {
                    System neighbourSys = EveManager.Instance.GetEveSystem(jump);
                    if (neighbourSys == null || IsExcluded(neighbourSys, avoidHighSec, avoid))
                    {
                        continue;
                    }

                    neighbours.Add(neighbourSys.Name);
                }

                adjacency[sys.Name] = neighbours;
            }

            return adjacency;
        }

        // TrueSec > 0.45 matches Navigation.InitNavigation's own HighSec flag and CreateStaticNavigationCache's
        // jump-exclusion threshold.
        private static bool IsExcluded(System sys, bool avoidHighSec, HashSet<string> avoid)
        {
            if (avoidHighSec && sys.TrueSec > 0.45)
            {
                return true;
            }

            return avoid.Contains(sys.Name);
        }
    }
}
