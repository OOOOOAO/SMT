namespace SMT.EVEData
{
    /// <summary>
    /// Result of planning a route across a batch of pasted bookmarks : see docs/spec-bookmark-route-planner.md.
    /// Independent of <see cref="LocalCharacter.Waypoints"/> / <see cref="LocalCharacter.ActiveRoute"/> by design (§5).
    /// </summary>
    public class BookmarkRoute
    {
        /// <summary>Route segments, cut wherever a capital jump connects two targets.</summary>
        public List<RouteLine> Lines { get; set; } = new List<RouteLine>();

        /// <summary>Target systems that could not be reached (gate-excluded and out of jump range, or islanded).</summary>
        public List<string> UnreachableSystems { get; set; } = new List<string>();

        /// <summary>
        /// Targets dropped because nothing else was within the isolation radius by gates. Reaching one of
        /// these is a trip of its own, and letting the router bend the whole route around it costs more than
        /// the point is worth.
        /// </summary>
        public List<string> IsolatedSystems { get; set; } = new List<string>();

        /// <summary>Raw text of pasted lines that did not resolve to a known system.</summary>
        public List<string> UnparsedLines { get; set; } = new List<string>();

        /// <summary>Resolved system name -> number of bookmarks that landed on it.</summary>
        public Dictionary<string, int> BookmarkCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Bookmarked systems the plan actually visits, counted off the finished route rather than added up
        /// from the drop lists. Derived the round-about way on purpose: if a target ever goes missing without
        /// landing in <see cref="IsolatedSystems"/> or <see cref="UnreachableSystems"/>, the discard rate
        /// reports it instead of hiding it.
        /// </summary>
        public int PlannedTargets { get; set; }

        /// <summary>Unique bookmarked systems parsed out of the pasted text.</summary>
        public int TotalTargets => BookmarkCounts.Count;

        /// <summary>Parsed but not in the plan: isolated, unreachable, or dropped for any other reason.</summary>
        public int DiscardedTargets => TotalTargets - PlannedTargets;

        /// <summary>Discarded share of parsed SYSTEMS, 0..1. Zero when nothing parsed.</summary>
        public double DiscardRate => TotalTargets == 0 ? 0.0 : (double)DiscardedTargets / TotalTargets;

        /// <summary>Bookmarks on the systems the plan visits. See <see cref="BookmarkDiscardRate"/>.</summary>
        public int PlannedBookmarks { get; set; }

        /// <summary>Total bookmarks parsed, counting every one -- systems commonly hold several.</summary>
        public int TotalBookmarks
        {
            get
            {
                int total = 0;
                foreach (int count in BookmarkCounts.Values)
                {
                    total += count;
                }

                return total;
            }
        }

        /// <summary>Bookmarks on systems the plan skips.</summary>
        public int DiscardedBookmarks => TotalBookmarks - PlannedBookmarks;

        /// <summary>
        /// Discarded share of parsed BOOKMARKS, 0..1. The honest headline number : systems hold anywhere from
        /// one to four bookmarks, so dropping one system is not worth a fixed slice of the job. Dropping a
        /// four-bookmark system reads as 1-of-42 by system and 4-of-68 by bookmark -- a threefold difference.
        /// </summary>
        public double BookmarkDiscardRate => TotalBookmarks == 0 ? 0.0 : (double)DiscardedBookmarks / TotalBookmarks;
    }

    /// <summary>One travel segment between two capital jumps (or from the start, for the first line).</summary>
    public class RouteLine
    {
        /// <summary>Full expanded system sequence for this line, including pass-through systems, for display/mapping.</summary>
        public List<Navigation.RoutePoint> Points { get; set; } = new List<Navigation.RoutePoint>();

        /// <summary>The target (bookmarked) systems visited by this line, in visiting order.</summary>
        public List<string> Targets { get; set; } = new List<string>();

        /// <summary>LY of the jump that started this line; 0 for the first line.</summary>
        public decimal EntryJumpLY { get; set; }

        public int TargetCount { get; set; }

        public int GateJumps { get; set; }
    }
}
