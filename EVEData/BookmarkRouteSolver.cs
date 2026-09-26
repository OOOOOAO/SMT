namespace SMT.EVEData
{
    /// <summary>
    /// Core route-planning algorithm for the bookmark route planner. Deliberately free of any
    /// dependency on <see cref="EveManager"/> or <see cref="Navigation"/> : the gate graph, LY lookup and
    /// system-name resolution are all passed in, so this can run against a small synthetic graph without
    /// touching the 33MB data files (see <see cref="BookmarkRouteSelfCheck"/>). <see cref="BookmarkRouteAdapter"/>
    /// wires the real game data in.
    ///
    /// One-phase hybrid cost matrix rather than gate-TSP-then-substitute-jumps :
    /// a two-phase approach can lock in a gate-optimal order that no later jump substitution can
    /// fix (covered by <see cref="BookmarkRouteSelfCheck"/>).
    /// </summary>
    public static class BookmarkRouteSolver
    {
        private const int Unreachable = int.MaxValue;

        /// <summary>
        /// Every cost is held at 100x so a jump can carry its length as a tie-break in the low digits.
        /// Without it all jumps priced flat at jumpCost tie exactly, and the tour picks between them by
        /// list order : an isolated target with eight in-range neighbours got entered from the 5.19 LY one
        /// instead of the 3.94 LY one for no reason at all. Shorter jumps are strictly better (less fatigue,
        /// and the drawn route stops looking arbitrary), and this costs nothing to prefer.
        /// </summary>
        private const int CostScale = 100;

        public static BookmarkRoute Solve(
            IReadOnlyDictionary<string, List<string>> adjacency,
            Func<string, string, decimal> lyLookup,
            string start,
            IEnumerable<string> targetSystems,
            Dictionary<string, int> bookmarkCounts,
            List<string> unparsedLines,
            int k,
            int jumpCost,
            decimal maxLY,
            int isolationJumps,
            int isolationKeepBookmarks)
        {
            BookmarkRoute result = new BookmarkRoute
            {
                UnparsedLines = unparsedLines != null ? new List<string>(unparsedLines) : new List<string>(),
                BookmarkCounts = bookmarkCounts != null ? new Dictionary<string, int>(bookmarkCounts) : new Dictionary<string, int>(),
            };

            // Seeded before any of the early returns below, so every exit path reports a usable count. A
            // bookmark on the start system counts as planned : you are already standing on it.
            result.PlannedTargets = result.BookmarkCounts.ContainsKey(start) ? 1 : 0;
            result.PlannedBookmarks = result.BookmarkCounts.GetValueOrDefault(start);

            // Dedupe + drop the start if it's also a target : already "visited".
            List<string> targets = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            foreach (string t in targetSystems)
            {
                if (t == start)
                {
                    continue;
                }

                if (seen.Add(t))
                {
                    targets.Add(t);
                }
            }

            if (targets.Count == 0)
            {
                return result;
            }

            // Node set : index 0 = start, 1..n = targets. One BFS per node (N+1 total), reused for every
            // matrix cell that node participates in, instead of one Dijkstra per pair.
            List<string> nodes = new List<string> { start };
            nodes.AddRange(targets);
            int nodeCount = nodes.Count;

            Dictionary<string, int>[] dist = new Dictionary<string, int>[nodeCount];
            Dictionary<string, string>[] prev = new Dictionary<string, string>[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                (dist[i], prev[i]) = Bfs(nodes[i], adjacency);
            }

            // Reachability and isolation are both settled here, before the cost matrix, so that emptyRun only
            // counts targets actually being visited.
            HashSet<int> dropped = new HashSet<int>();
            for (int i = 1; i < nodeCount; i++)
            {
                // A target the gates can't reach from the start can't be reached by any gate/jump combination
                // either -- a jump only ever connects two already gate-reachable points -- so one check against
                // row 0 settles the genuinely unreachable set.
                if (!dist[0].ContainsKey(nodes[i]))
                {
                    result.UnreachableSystems.Add(nodes[i]);
                    dropped.Add(i);
                }
            }

            if (isolationJumps > 0)
            {
                // A target with nothing else inside isolationJumps GATE jumps is a trip of its own. Gate jumps
                // only is the whole point : "but it's in capital jump range" is exactly the case being ruled
                // out. The start counts as company -- a lone bookmark next door to where you're already sitting
                // isn't a detour. Single pass is enough: a mutually-close pair keeps each other, and a node is
                // only ever dropped once it has no undropped company at all.
                for (int i = 1; i < nodeCount; i++)
                {
                    if (dropped.Contains(i))
                    {
                        continue;
                    }

                    // Several bookmarks in one system is its own reason to make the trip, so a bookmark-heavy
                    // system is never the lone outlier this filter exists to catch. Guarded on > 0 because the
                    // obvious "count >= 0" reading would protect everything and quietly disable the filter.
                    if (isolationKeepBookmarks > 0 &&
                        result.BookmarkCounts.GetValueOrDefault(nodes[i]) >= isolationKeepBookmarks)
                    {
                        continue;
                    }

                    bool hasCompany = false;
                    for (int j = 0; j < nodeCount && !hasCompany; j++)
                    {
                        if (i == j || dropped.Contains(j))
                        {
                            continue;
                        }

                        hasCompany = dist[i].GetValueOrDefault(nodes[j], Unreachable) <= isolationJumps;
                    }

                    if (!hasCompany)
                    {
                        result.IsolatedSystems.Add(nodes[i]);
                        dropped.Add(i);
                    }
                }
            }

            HashSet<string> targetSet = new HashSet<string>();
            for (int i = 1; i < nodeCount; i++)
            {
                if (!dropped.Contains(i))
                {
                    targetSet.Add(nodes[i]);
                }
            }

            int[,] cost = new int[nodeCount, nodeCount];
            decimal[,] ly = new decimal[nodeCount, nodeCount];
            bool[,] isJump = new bool[nodeCount, nodeCount];

            for (int i = 0; i < nodeCount; i++)
            {
                for (int j = 0; j < nodeCount; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    int gateDist = dist[i].GetValueOrDefault(nodes[j], Unreachable);

                    if (gateDist == Unreachable)
                    {
                        cost[i, j] = Unreachable;
                        continue;
                    }

                    decimal lyValue = lyLookup(nodes[i], nodes[j]);
                    ly[i, j] = lyValue;

                    // The start is jump-eligible like any other node : staged 40 gates out but 5 LY away is
                    // exactly when a capital jumps in, and forcing the gate trip there would be wrong. A jump
                    // on the first leg leaves a start-only line, which BuildLines drops.
                    int emptyRun = 0;
                    if (gateDist >= 2)
                    {
                        emptyRun = LongestNonTargetRun(nodes[i], nodes[j], prev[i], targetSet);
                    }

                    // jumpCost is what stops the optimiser chaining jumps. Priced at 1 a jump was the cheapest
                    // edge on the board, so a 10-gate detour collapsed below a 2-gate hop and the tour would
                    // jump, jump, jump -- one target per line, the opposite of "maximise the points per line".
                    // Charging jumpCost, and only taking the jump when it actually beats gating, keeps a jump
                    // for the legs where the gate network really is the long way round.
                    if (emptyRun >= k && lyValue > 0 && lyValue <= maxLY && jumpCost < gateDist)
                    {
                        // + LY as a tie-break in the sub-CostScale digits : see CostScale.
                        cost[i, j] = (jumpCost * CostScale) + (int)(lyValue * 10m);
                        isJump[i, j] = true;
                    }
                    else
                    {
                        cost[i, j] = gateDist * CostScale;
                    }
                }
            }

            List<int> activeIdx = new List<int>();
            for (int i = 1; i < nodeCount; i++)
            {
                if (!dropped.Contains(i))
                {
                    activeIdx.Add(i);
                }
            }

            if (activeIdx.Count == 0)
            {
                return result;
            }

            List<int> tour = NearestNeighbourTour(cost, activeIdx);
            ImproveTour(cost, tour);

            BuildLines(result, nodes, ly, isJump, prev, tour);

            foreach (RouteLine line in result.Lines)
            {
                result.PlannedTargets += line.Targets.Count;

                foreach (string target in line.Targets)
                {
                    result.PlannedBookmarks += result.BookmarkCounts.GetValueOrDefault(target);
                }
            }

            return result;
        }

        private static (Dictionary<string, int> dist, Dictionary<string, string> prev) Bfs(
            string source, IReadOnlyDictionary<string, List<string>> adjacency)
        {
            Dictionary<string, int> dist = new Dictionary<string, int> { [source] = 0 };
            Dictionary<string, string> prev = new Dictionary<string, string>();

            if (!adjacency.ContainsKey(source))
            {
                return (dist, prev);
            }

            Queue<string> queue = new Queue<string>();
            queue.Enqueue(source);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!adjacency.TryGetValue(current, out List<string> neighbours))
                {
                    continue;
                }

                foreach (string next in neighbours)
                {
                    if (dist.ContainsKey(next))
                    {
                        continue;
                    }

                    dist[next] = dist[current] + 1;
                    prev[next] = current;
                    queue.Enqueue(next);
                }
            }

            return (dist, prev);
        }

        private static List<string> ReconstructPath(string source, string target, Dictionary<string, string> prev)
        {
            List<string> path = new List<string> { target };
            string current = target;
            while (current != source)
            {
                current = prev[current];
                path.Add(current);
            }

            path.Reverse();
            return path;
        }

        /// <summary>Longest run of consecutive non-target systems on the shortest gate path from i to j.</summary>
        private static int LongestNonTargetRun(string i, string j, Dictionary<string, string> prevFromI, HashSet<string> targetSet)
        {
            List<string> path = ReconstructPath(i, j, prevFromI);

            int run = 0;
            int maxRun = 0;
            for (int p = 1; p < path.Count - 1; p++)
            {
                if (targetSet.Contains(path[p]))
                {
                    run = 0;
                }
                else
                {
                    run++;
                    if (run > maxRun)
                    {
                        maxRun = run;
                    }
                }
            }

            return maxRun;
        }

        private static List<int> NearestNeighbourTour(int[,] cost, List<int> activeIdx)
        {
            List<int> tour = new List<int> { 0 };
            List<int> remaining = new List<int>(activeIdx);
            int current = 0;

            while (remaining.Count > 0)
            {
                int best = remaining[0];
                foreach (int candidate in remaining)
                {
                    if (cost[current, candidate] < cost[current, best])
                    {
                        best = candidate;
                    }
                }

                tour.Add(best);
                remaining.Remove(best);
                current = best;
            }

            return tour;
        }

        /// <summary>Cost of walking the tour in order. long, so an Unreachable cell cannot overflow the sum.</summary>
        internal static long TourCost(int[,] cost, List<int> tour)
        {
            long total = 0;
            for (int i = 1; i < tour.Count; i++)
            {
                total += cost[tour[i - 1], tour[i]];
            }

            return total;
        }

        /// <summary>
        /// Takes the nearest-neighbour tour to a much better local optimum than 2-opt alone reaches.
        ///
        /// 2-opt only ever reverses a stretch of the tour. It cannot pick up one target and put it somewhere
        /// else, which is the move a stranded target needs : on a live 25-system paste it left
        /// KL3O-J -> jump -> I-7RIS -> jump -> BWI1-9 (a two-jump detour for one system) and
        /// C-LP3N -> jump -> 4-48K1 -> jump back -> 0P9Z-I in the plan. Or-opt moves 1..3 consecutive
        /// targets to their cheapest slot, which is exactly that move, and the two alternate until neither
        /// helps.
        ///
        /// A local optimum is still only local, and which one the search lands in depends on where it
        /// started : putting the start system on the avoid list changed a few gate distances, the
        /// nearest-neighbour seed with them, and the plan came out visibly different although the best tour
        /// was the same one in both cases. So the local optimum is then kicked (double bridge) and re-searched
        /// a bounded number of times, keeping the best. The generator is seeded, so the same paste and settings
        /// always give the same plan.
        /// </summary>
        internal static void ImproveTour(int[,] cost, List<int> tour)
        {
            long best = LocalSearch(cost, tour);

            // A double bridge needs four distinct cut points after the fixed start.
            int n = tour.Count;
            if (n < 5)
            {
                return;
            }

            // Each round is a full local search, O(n^2) per pass, so the round count shrinks as the tour grows
            // to keep a large paste from stalling the panel. 100 rounds at the usual 25..100 targets.
            int rounds = Math.Clamp(2_000_000 / (n * n), 5, 100);
            Random random = new Random(20260926);

            for (int round = 0; round < rounds; round++)
            {
                List<int> candidate = DoubleBridge(tour, random);
                long candidateCost = LocalSearch(cost, candidate);

                if (candidateCost < best)
                {
                    best = candidateCost;
                    tour.Clear();
                    tour.AddRange(candidate);
                }
            }
        }

        /// <summary>2-opt and Or-opt in turn until neither finds anything. Returns the final tour cost.</summary>
        internal static long LocalSearch(int[,] cost, List<int> tour)
        {
            const int maxIterations = 1000; // ponytail: guards pathological inputs
            for (int iterations = 0; iterations < maxIterations; iterations++)
            {
                bool improved = TwoOptImprove(cost, tour);
                improved |= OrOptImprove(cost, tour);

                if (!improved)
                {
                    break;
                }
            }

            return TourCost(cost, tour);
        }

        /// <summary>
        /// Cuts the open tour into start + A B C D + tail and reorders it to start + C B A + D-and-tail. No
        /// 2-opt or Or-opt move undoes this in one step, which is what lets the next local search leave the
        /// optimum it was stuck in.
        /// </summary>
        private static List<int> DoubleBridge(List<int> tour, Random random)
        {
            int n = tour.Count;

            // Four distinct cut points in 1..n : a < b < c < d. d == n puts the whole tail in the last segment.
            SortedSet<int> cuts = new SortedSet<int>();
            while (cuts.Count < 4)
            {
                cuts.Add(random.Next(1, n + 1));
            }

            int[] c = new int[4];
            cuts.CopyTo(c);

            List<int> result = new List<int>(n);
            result.AddRange(tour.GetRange(0, c[0]));
            result.AddRange(tour.GetRange(c[2], c[3] - c[2]));
            result.AddRange(tour.GetRange(c[1], c[2] - c[1]));
            result.AddRange(tour.GetRange(c[0], c[1] - c[0]));
            result.AddRange(tour.GetRange(c[3], n - c[3]));
            return result;
        }

        /// <summary>
        /// Open-path 2-opt : start (index 0) fixed, end free. Iterates to a local optimum or a safety cap.
        /// Returns whether it changed the tour.
        ///
        /// The matrix is not symmetric : emptyRun(i,j) is read off the BFS tree rooted at i while emptyRun(j,i)
        /// comes off the tree rooted at j, so when a pair has more than one shortest gate path the two directions
        /// can disagree about whether the leg is jump-eligible. Reversing a segment flips every interior edge,
        /// so the usual two-boundary-edge delta is wrong here : it never looks at the interior and can accept a
        /// swap that makes the tour worse (D27). The delta below does price the interior, both ways round,
        /// accumulated as j walks outwards, so it stays exact at O(1) per candidate.
        /// </summary>
        internal static bool TwoOptImprove(int[,] cost, List<int> tour)
        {
            const int maxIterations = 1000; // ponytail: guards pathological inputs
            int n = tour.Count;
            bool changed = false;
            bool improved = true;
            int iterations = 0;

            while (improved && iterations < maxIterations)
            {
                improved = false;
                iterations++;

                for (int i = 1; i < n - 1; i++)
                {
                    long forward = 0;
                    long backward = 0;

                    for (int j = i + 1; j < n; j++)
                    {
                        // Interior edges of tour[i..j], as walked now and as walked after the reversal.
                        forward += cost[tour[j - 1], tour[j]];
                        backward += cost[tour[j], tour[j - 1]];

                        long before = cost[tour[i - 1], tour[i]] + forward;
                        long after = cost[tour[i - 1], tour[j]] + backward;
                        if (j + 1 < n)
                        {
                            before += cost[tour[j], tour[j + 1]];
                            after += cost[tour[i], tour[j + 1]];
                        }

                        if (after < before)
                        {
                            tour.Reverse(i, j - i + 1);
                            improved = true;
                            changed = true;

                            // The sums were for the old order of this stretch.
                            forward = 0;
                            backward = 0;
                            for (int p = i + 1; p <= j; p++)
                            {
                                forward += cost[tour[p - 1], tour[p]];
                                backward += cost[tour[p], tour[p - 1]];
                            }
                        }
                    }
                }
            }

            return changed;
        }

        /// <summary>
        /// Or-opt : moves a run of 1..3 consecutive targets to the slot where it costs least, as is or reversed.
        /// Takes the first improving move and rescans, until no move improves. Returns whether it changed the tour.
        ///
        /// Exact at O(1) per candidate on the asymmetric matrix : the run's interior edges keep their direction
        /// unless the run is reversed, and a reversed run's interior is priced both ways round explicitly.
        /// </summary>
        internal static bool OrOptImprove(int[,] cost, List<int> tour)
        {
            const int maxMoves = 10000; // ponytail: guards pathological inputs
            int n = tour.Count;
            bool changed = false;

            for (int moves = 0; moves < maxMoves; moves++)
            {
                if (!TryOrOptMove(cost, tour, n))
                {
                    break;
                }

                changed = true;
            }

            return changed;
        }

        private static bool TryOrOptMove(int[,] cost, List<int> tour, int n)
        {
            for (int len = 1; len <= 3; len++)
            {
                for (int i = 1; i + len <= n; i++)
                {
                    int last = i + len - 1;
                    int first = tour[i];
                    int end = tour[last];
                    int before = tour[i - 1];
                    bool hasAfter = last + 1 < n;
                    int after = hasAfter ? tour[last + 1] : -1;

                    // What taking the run out saves : its two boundary edges, less the edge that closes the gap.
                    long removed = cost[before, first];
                    if (hasAfter)
                    {
                        removed += (long)cost[end, after] - cost[before, after];
                    }

                    long interiorForward = 0;
                    long interiorBackward = 0;
                    for (int p = i; p < last; p++)
                    {
                        interiorForward += cost[tour[p], tour[p + 1]];
                        interiorBackward += cost[tour[p + 1], tour[p]];
                    }

                    // Insert between tour[p] and its successor in the tour with the run taken out. p == i - 1 is
                    // where the run already sits, and p inside the run is not a slot at all.
                    for (int p = 0; p < n; p++)
                    {
                        if (p >= i - 1 && p <= last)
                        {
                            continue;
                        }

                        int a = tour[p];
                        bool hasB = p + 1 < n;
                        int b = hasB ? tour[p + 1] : -1;

                        // long throughout : two Unreachable cells would overflow an int sum.
                        long gap = hasB ? cost[a, b] : 0L;
                        long asIs = (long)cost[a, first] + (hasB ? cost[end, b] : 0L) - gap;
                        long reversed = len > 1
                            ? (long)cost[a, end] + (hasB ? cost[first, b] : 0L) - gap + interiorBackward - interiorForward
                            : long.MaxValue;

                        bool reverse = reversed < asIs;
                        long added = reverse ? reversed : asIs;

                        if (added < removed)
                        {
                            List<int> run = tour.GetRange(i, len);
                            if (reverse)
                            {
                                run.Reverse();
                            }

                            tour.RemoveRange(i, len);
                            int insertAt = (p < i ? p : p - len) + 1;
                            tour.InsertRange(insertAt, run);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static void BuildLines(
            BookmarkRoute result, List<string> nodes, decimal[,] ly, bool[,] isJump,
            Dictionary<string, string>[] prev, List<int> tour)
        {
            RouteLine current = new RouteLine();
            current.Points.Add(new Navigation.RoutePoint { SystemName = nodes[tour[0]], GateToTake = Navigation.GateType.StarGate });

            for (int idx = 1; idx < tour.Count; idx++)
            {
                int a = tour[idx - 1];
                int b = tour[idx];

                if (isJump[a, b])
                {
                    result.Lines.Add(current);

                    current = new RouteLine { EntryJumpLY = ly[a, b] };
                    current.Points.Add(new Navigation.RoutePoint { SystemName = nodes[b], GateToTake = Navigation.GateType.JumpTo, LY = ly[a, b] });
                }
                else
                {
                    List<string> path = ReconstructPath(nodes[a], nodes[b], prev[a]);
                    for (int p = 1; p < path.Count; p++)
                    {
                        current.Points.Add(new Navigation.RoutePoint { SystemName = path[p], GateToTake = Navigation.GateType.StarGate });
                    }
                }

                current.Targets.Add(nodes[b]);
            }

            result.Lines.Add(current);

            // Jumping on the very first leg leaves a line holding only the start system and nothing to visit.
            // Drop it : the jump is still on record as the next line's EntryJumpLY.
            if (result.Lines.Count > 1 && result.Lines[0].Targets.Count == 0)
            {
                result.Lines.RemoveAt(0);
            }

            foreach (RouteLine line in result.Lines)
            {
                line.TargetCount = line.Targets.Count;
                line.GateJumps = line.Points.Count - 1;
            }
        }
    }
}
