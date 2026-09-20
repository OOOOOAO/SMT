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

            Dictionary<string, List<string>> adjacency = BuildAdjacency(EveManager.Instance.Systems, EveManager.Instance.GetEveSystem, avoidHighSec, avoid, startSystem);

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
        //
        // The start is the one exception : it's kept as a node even when the filters exclude it. The user
        // picks the start freely now, and "start from the system I'm actually sitting in" is the whole
        // point -- refusing to route out of a system that happens to be high sec, or that the user put on
        // their own avoid list, would be worse than routing out of it. Its neighbours still obey the
        // filters, so a route leaves an excluded start without ever transiting another excluded system.
        //
        // Takes the system list and the name resolver as arguments rather than reading EveManager, so the
        // start-exemption rule can be self-checked against a synthetic galaxy (see BookmarkRouteSelfCheck).
        internal static Dictionary<string, List<string>> BuildAdjacency(
            IEnumerable<System> systems, Func<string, System> resolveSystem, bool avoidHighSec, HashSet<string> avoid, string startSystem)
        {
            Dictionary<string, List<string>> adjacency = new Dictionary<string, List<string>>();

            foreach (System sys in systems)
            {
                if (IsExcluded(sys, avoidHighSec, avoid) && !IsStart(sys, startSystem))
                {
                    continue;
                }

                List<string> neighbours = new List<string>();
                foreach (string jump in sys.Jumps)
                {
                    System neighbourSys = resolveSystem(jump);
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

        // The start is matched case-insensitively to agree with NameToSystem, which GetEveSystem reads.
        private static bool IsStart(System sys, string startSystem)
        {
            return !string.IsNullOrEmpty(startSystem) &&
                string.Equals(sys.Name, startSystem, StringComparison.OrdinalIgnoreCase);
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
