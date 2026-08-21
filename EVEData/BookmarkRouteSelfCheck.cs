namespace SMT.EVEData
{
    /// <summary>
    /// Assert-based self-check for <see cref="BookmarkRouteSolver"/> and <see cref="BookmarkParser"/>, run on
    /// small synthetic graphs so it needs no test framework and touches none of the 33MB data files.
    /// Invoke via <c>DataGen selfcheck</c> (see DataGen/Program.cs).
    /// </summary>
    public static class BookmarkRouteSelfCheck
    {
        public static bool Run()
        {
            (string Name, Action Check)[] checks =
            {
                ("hybrid cost matrix counter-example", CheckHybridCostCounterExample),
                ("line splitting on jump legs", CheckLineSplitting),
                ("distance-zero vs unreachable are distinguishable", CheckDistanceZeroVsUnreachable),
                ("parser takes column 3, not a name-column decoy", CheckParserColumnPriority),
                ("a jump is allowed on the first leg, out of the start", CheckJumpFromStart),
                ("2-opt never worsens a tour on an asymmetric matrix", CheckTwoOptOnAsymmetricCost),
                ("jump cost stops jumps chaining back to back", CheckNoChainedJumps),
                ("isolated targets are dropped, and only when the filter is on", CheckIsolatedTargetDropped),
                ("discard rate counts what the plan left behind", CheckDiscardRate),
                ("a bookmark-heavy system survives the isolation filter", CheckBookmarkHeavyTargetKept),
            };

            bool allPassed = true;
            foreach ((string name, Action check) in checks)
            {
                try
                {
                    check();
                    Console.WriteLine($"PASS: {name}");
                }
                catch (Exception ex)
                {
                    allPassed = false;
                    Console.WriteLine($"FAIL: {name} -- {ex.Message}");
                }
            }

            Console.WriteLine(allPassed ? "ALL CHECKS PASSED" : "SOME CHECKS FAILED");
            return allPassed;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        // Builds a chain from -> dummy1 -> dummy2 -> ... -> to with `hops` edges total (hops-1 dummy nodes).
        private static void AddChain(Dictionary<string, List<string>> adj, string from, string to, int hops, string dummyPrefix)
        {
            string prev = from;
            for (int i = 1; i < hops; i++)
            {
                string node = dummyPrefix + i;
                AddEdge(adj, prev, node);
                prev = node;
            }

            AddEdge(adj, prev, to);
        }

        private static void AddEdge(Dictionary<string, List<string>> adj, string a, string b)
        {
            if (!adj.TryGetValue(a, out List<string> la))
            {
                la = new List<string>();
                adj[a] = la;
            }

            la.Add(b);

            if (!adj.TryGetValue(b, out List<string> lb))
            {
                lb = new List<string>();
                adj[b] = lb;
            }

            lb.Add(a);
        }

        /// <summary>
        /// Targets A, B, C from start S. gate(A,B)=30 but A/B are 5 LY apart (jumpable) ;
        /// gate(A,C)=gate(C,B)=12, neither pair jumpable (8 LY, over the 6 LY range). The one-phase hybrid
        /// matrix must produce S→A→B→C (A→B collapses to a jump), not the gate-optimal-looking S→A→C→B.
        /// </summary>
        private static void CheckHybridCostCounterExample()
        {
            Dictionary<string, List<string>> adj = new Dictionary<string, List<string>>();
            AddChain(adj, "S", "A", 1, "sa");
            AddChain(adj, "A", "B", 30, "ab");
            AddChain(adj, "A", "C", 12, "ac");
            AddChain(adj, "C", "B", 12, "cb");

            decimal Ly(string a, string b)
            {
                if ((a == "A" && b == "B") || (a == "B" && b == "A")) return 5m;
                if ((a == "A" && b == "C") || (a == "C" && b == "A")) return 8m;
                if ((a == "C" && b == "B") || (a == "B" && b == "C")) return 8m;
                return 999m;
            }

            List<string> targets = new List<string> { "A", "B", "C" };
            Dictionary<string, int> counts = new Dictionary<string, int> { ["A"] = 1, ["B"] = 1, ["C"] = 1 };

            BookmarkRoute route = BookmarkRouteSolver.Solve(adj, Ly, "S", targets, counts, new List<string>(), 2, 5, 6m, 0, 0);

            Assert(route.UnreachableSystems.Count == 0, "expected no unreachable systems");

            List<string> order = new List<string>();
            foreach (RouteLine line in route.Lines)
            {
                order.AddRange(line.Targets);
            }

            Assert(
                order.Count == 3 && order[0] == "A" && order[1] == "B" && order[2] == "C",
                $"expected visiting order A,B,C but got [{string.Join(",", order)}]");

            Assert(route.Lines.Count == 2, $"expected 2 lines (cut at the A->B jump), got {route.Lines.Count}");
            Assert(route.Lines[1].EntryJumpLY == 5m, $"expected line 2's entry jump to be 5 LY, got {route.Lines[1].EntryJumpLY}");
        }

        private static void CheckLineSplitting()
        {
            // Two clusters {T1,T2} / {T3,T4}, 5 LY apart, joined only by a long empty gate chain => 1 jump, 2 lines.
            Dictionary<string, List<string>> adj = new Dictionary<string, List<string>>();
            AddChain(adj, "S", "T1", 1, "st1");
            AddChain(adj, "T1", "T2", 1, "t1t2");
            AddChain(adj, "T2", "T3", 10, "gap");
            AddChain(adj, "T3", "T4", 1, "t3t4");

            decimal Ly(string a, string b) => (a == "T2" && b == "T3") || (a == "T3" && b == "T2") ? 5m : 999m;

            List<string> targets = new List<string> { "T1", "T2", "T3", "T4" };
            Dictionary<string, int> counts = new Dictionary<string, int> { ["T1"] = 1, ["T2"] = 1, ["T3"] = 1, ["T4"] = 1 };

            BookmarkRoute twoClusters = BookmarkRouteSolver.Solve(adj, Ly, "S", targets, counts, new List<string>(), 2, 5, 6m, 0, 0);
            Assert(twoClusters.Lines.Count == 2, $"two-cluster case: expected 2 lines, got {twoClusters.Lines.Count}");

            // A single cluster with no viable jump anywhere should stay as one line.
            Dictionary<string, List<string>> adjSingle = new Dictionary<string, List<string>>();
            AddChain(adjSingle, "S", "T1", 1, "s1");
            AddChain(adjSingle, "T1", "T2", 1, "one2");
            AddChain(adjSingle, "T2", "T3", 1, "two3");

            BookmarkRoute single = BookmarkRouteSolver.Solve(
                adjSingle, (a, b) => 999m, "S", new List<string> { "T1", "T2", "T3" },
                new Dictionary<string, int> { ["T1"] = 1, ["T2"] = 1, ["T3"] = 1 }, new List<string>(), 2, 5, 6m, 0, 0);
            Assert(single.Lines.Count == 1, $"single-cluster case: expected 1 line, got {single.Lines.Count}");
        }

        private static void CheckDistanceZeroVsUnreachable()
        {
            Dictionary<string, List<string>> adj = new Dictionary<string, List<string>>();
            AddChain(adj, "S", "T1", 1, "st1");
            adj["Isolated"] = new List<string>(); // present in the graph, but zero connections -- genuinely unreachable

            List<string> targets = new List<string> { "T1", "Isolated" };
            Dictionary<string, int> counts = new Dictionary<string, int> { ["T1"] = 1, ["Isolated"] = 1 };

            BookmarkRoute route = BookmarkRouteSolver.Solve(adj, (a, b) => 999m, "S", targets, counts, new List<string>(), 2, 5, 6m, 0, 0);

            Assert(
                route.UnreachableSystems.Count == 1 && route.UnreachableSystems[0] == "Isolated",
                $"expected only Isolated to be unreachable, got [{string.Join(",", route.UnreachableSystems)}]");
            Assert(
                route.Lines.Count == 1 && route.Lines[0].GateJumps == 1,
                $"expected T1 reached with exactly 1 gate jump, got {(route.Lines.Count > 0 ? route.Lines[0].GateJumps : -1)}");
        }

        /// <summary>
        /// The start system is jump-eligible like any other node. Staged 10 gates away from the first
        /// target but only 5 LY from it, the plan must jump rather than fly the gates. The start-only line
        /// that the jump leaves behind is dropped, and the jump survives as line 1's EntryJumpLY.
        /// </summary>
        private static void CheckJumpFromStart()
        {
            Dictionary<string, List<string>> adj = new Dictionary<string, List<string>>();
            AddChain(adj, "S", "T1", 10, "gap");
            AddChain(adj, "T1", "T2", 1, "t1t2");

            decimal Ly(string a, string b) => (a == "S" && b == "T1") || (a == "T1" && b == "S") ? 5m : 999m;

            BookmarkRoute route = BookmarkRouteSolver.Solve(
                adj, Ly, "S", new List<string> { "T1", "T2" },
                new Dictionary<string, int> { ["T1"] = 1, ["T2"] = 1 }, new List<string>(), 2, 5, 6m, 0, 0);

            Assert(route.Lines.Count == 1, $"expected the start-only line to be dropped, leaving 1 line, got {route.Lines.Count}");
            Assert(
                route.Lines[0].EntryJumpLY == 5m,
                $"expected line 1 to be entered by a 5 LY jump out of the start, got {route.Lines[0].EntryJumpLY} (0 means the gate route was taken instead)");
            Assert(
                route.Lines[0].Targets.Count == 2 && route.Lines[0].Targets[0] == "T1" && route.Lines[0].Targets[1] == "T2",
                $"expected targets T1,T2 but got [{string.Join(",", route.Lines[0].Targets)}]");
        }

        /// <summary>
        /// The cost matrix is not symmetric, so 2-opt has to score candidates by re-measuring the whole
        /// tour. Here the two boundary edges look 18 cheaper after the swap (10+10 becomes 1+1), but reversing
        /// the segment flips the interior edge from cost 1 to cost 100 -- which the old two-edge delta never
        /// looked at. It would swap 21 into 102 and keep going ; the fixed version leaves the tour alone.
        /// </summary>
        /// <summary>
        /// Modelled on a real case from Branch that the user hit: KJ-QWL and 5-P1Y2 are 2 gates apart, but each
        /// is separately within jump range of I-7RIS across a 10-12 gate gap. Priced at 1 a jump was cheaper
        /// than any gate travel at all, so the tour went KJ-QWL -> jump -> I-7RIS -> jump -> 5-P1Y2 (cost 2)
        /// instead of gating the two hops and jumping once (cost 3), producing single-target lines. With a jump
        /// charged properly the two-hop gate leg wins and the chain disappears.
        /// </summary>
        private static void CheckNoChainedJumps()
        {
            Dictionary<string, List<string>> adj = new Dictionary<string, List<string>>();
            AddChain(adj, "S", "A", 1, "sa");
            AddChain(adj, "A", "C", 2, "ac");   // 1 empty system between : emptyRun 1, never jump-eligible at K=2
            AddChain(adj, "A", "B", 10, "ab");  // 9 empties : jump-eligible, and C->B runs 12 gates through A

            decimal Ly(string a, string b)
            {
                if ((a == "A" && b == "B") || (a == "B" && b == "A")) return 5m;
                if ((a == "C" && b == "B") || (a == "B" && b == "C")) return 4m;
                if ((a == "A" && b == "C") || (a == "C" && b == "A")) return 2m;
                return 999m;
            }

            BookmarkRoute route = BookmarkRouteSolver.Solve(
                adj, Ly, "S", new List<string> { "A", "B", "C" },
                new Dictionary<string, int> { ["A"] = 1, ["B"] = 1, ["C"] = 1 }, new List<string>(), 2, 5, 6m, 0, 0);

            List<string> order = new List<string>();
            foreach (RouteLine line in route.Lines)
            {
                order.AddRange(line.Targets);
            }

            Assert(
                order.Count == 3 && order[0] == "A" && order[1] == "C" && order[2] == "B",
                $"expected A,C,B -- gate the 2 hops to C, then jump once -- but got [{string.Join(",", order)}]");
            Assert(
                route.Lines.Count == 2,
                $"expected 2 lines (one jump), got {route.Lines.Count} : back-to-back jumps are splitting the route into single-target lines");
        }

        /// <summary>
        /// A and B sit next to each other ; Z is 8 gates past B with nothing near it. Modelled on KMC-WI in
        /// Branch, whose nearest other bookmark was 8 gates away while all 41 others had company within 2.
        /// With the filter on, Z is dropped rather than dragging the route across the map ; with it off
        /// (isolationJumps 0) Z is still visited, so the filter can't be silently always-on.
        /// </summary>
        private static void CheckIsolatedTargetDropped()
        {
            Dictionary<string, List<string>> adj = new Dictionary<string, List<string>>();
            AddChain(adj, "S", "A", 1, "sa");
            AddChain(adj, "A", "B", 1, "ab");
            AddChain(adj, "B", "Z", 8, "bz");

            List<string> targets = new List<string> { "A", "B", "Z" };
            Dictionary<string, int> counts = new Dictionary<string, int> { ["A"] = 1, ["B"] = 1, ["Z"] = 1 };

            BookmarkRoute filtered = BookmarkRouteSolver.Solve(
                adj, (x, y) => 999m, "S", targets, counts, new List<string>(), 2, 5, 6m, 5, 0);

            Assert(
                filtered.IsolatedSystems.Count == 1 && filtered.IsolatedSystems[0] == "Z",
                $"expected Z dropped as isolated, got [{string.Join(",", filtered.IsolatedSystems)}]");

            List<string> visited = new List<string>();
            foreach (RouteLine line in filtered.Lines)
            {
                visited.AddRange(line.Targets);
            }

            Assert(
                visited.Count == 2 && !visited.Contains("Z"),
                $"expected only A and B visited, got [{string.Join(",", visited)}]");

            BookmarkRoute unfiltered = BookmarkRouteSolver.Solve(
                adj, (x, y) => 999m, "S", targets, counts, new List<string>(), 2, 5, 6m, 0, 0);

            Assert(
                unfiltered.IsolatedSystems.Count == 0,
                "with the filter off nothing should be dropped as isolated");
        }

        /// <summary>
        /// Discard rate over the same A/B/Z shape as the isolation check: 3 parsed, 2 planned, 1 discarded.
        /// Second half covers the subtle case -- a bookmark sitting on the start system is dropped from the
        /// target list because you are already there, and must still count as planned, not as discarded.
        /// </summary>
        private static void CheckDiscardRate()
        {
            Dictionary<string, List<string>> adj = new Dictionary<string, List<string>>();
            AddChain(adj, "S", "A", 1, "sa");
            AddChain(adj, "A", "B", 1, "ab");
            AddChain(adj, "B", "Z", 8, "bz");

            BookmarkRoute r = BookmarkRouteSolver.Solve(
                adj, (x, y) => 999m, "S", new List<string> { "A", "B", "Z" },
                new Dictionary<string, int> { ["A"] = 2, ["B"] = 1, ["Z"] = 3 }, new List<string>(), 2, 5, 6m, 5, 0);

            Assert(r.TotalTargets == 3, $"expected 3 parsed, got {r.TotalTargets}");
            Assert(r.PlannedTargets == 2, $"expected 2 planned, got {r.PlannedTargets}");
            Assert(r.DiscardedTargets == 1, $"expected 1 discarded, got {r.DiscardedTargets}");
            Assert(
                r.DiscardRate > 0.33 && r.DiscardRate < 0.34,
                $"expected a system discard rate of about 1/3, got {r.DiscardRate}");

            // Same run weighted by bookmarks: A holds 2 and B holds 1, but the dropped Z holds 3, so half the
            // actual job is being skipped while the per-system rate reads a third. Counting systems alone
            // under-reports whenever the dropped ones are bookmark-heavy.
            Assert(r.TotalBookmarks == 6, $"expected 6 bookmarks parsed, got {r.TotalBookmarks}");
            Assert(r.PlannedBookmarks == 3, $"expected 3 bookmarks planned, got {r.PlannedBookmarks}");
            Assert(
                r.BookmarkDiscardRate == 0.5,
                $"expected a bookmark discard rate of 0.5, got {r.BookmarkDiscardRate}");

            // A bookmark on the start system: 2 parsed, both planned, nothing discarded.
            Dictionary<string, List<string>> adj2 = new Dictionary<string, List<string>>();
            AddChain(adj2, "S", "A", 1, "sa2");

            BookmarkRoute onStart = BookmarkRouteSolver.Solve(
                adj2, (x, y) => 999m, "S", new List<string> { "S", "A" },
                new Dictionary<string, int> { ["S"] = 1, ["A"] = 1 }, new List<string>(), 2, 5, 6m, 0, 0);

            Assert(onStart.TotalTargets == 2, $"expected 2 parsed, got {onStart.TotalTargets}");
            Assert(
                onStart.PlannedTargets == 2,
                $"a bookmark on the start system must count as planned, not discarded : planned {onStart.PlannedTargets} of 2");
            Assert(onStart.DiscardRate == 0.0, $"expected a 0 discard rate, got {onStart.DiscardRate}");
        }

        /// <summary>
        /// Same isolated Z, but now holding 3 bookmarks. Three objectives in one place is worth the trip, so
        /// with the keep threshold at 3 it survives ; raise the threshold past what it holds and it goes back
        /// to being dropped. Without this the filter treats a 4-bookmark system exactly like a 1-bookmark one.
        /// </summary>
        private static void CheckBookmarkHeavyTargetKept()
        {
            Dictionary<string, List<string>> adj = new Dictionary<string, List<string>>();
            AddChain(adj, "S", "A", 1, "sa");
            AddChain(adj, "A", "B", 1, "ab");
            AddChain(adj, "B", "Z", 8, "bz");

            List<string> targets = new List<string> { "A", "B", "Z" };
            Dictionary<string, int> counts = new Dictionary<string, int> { ["A"] = 1, ["B"] = 1, ["Z"] = 3 };

            BookmarkRoute kept = BookmarkRouteSolver.Solve(
                adj, (x, y) => 999m, "S", targets, counts, new List<string>(), 2, 5, 6m, 5, 3);

            Assert(
                kept.IsolatedSystems.Count == 0,
                $"Z holds 3 bookmarks and the keep threshold is 3, so it must survive : dropped [{string.Join(",", kept.IsolatedSystems)}]");
            Assert(kept.PlannedTargets == 3, $"expected all 3 systems planned, got {kept.PlannedTargets}");

            BookmarkRoute dropped = BookmarkRouteSolver.Solve(
                adj, (x, y) => 999m, "S", targets, counts, new List<string>(), 2, 5, 6m, 5, 4);

            Assert(
                dropped.IsolatedSystems.Count == 1 && dropped.IsolatedSystems[0] == "Z",
                $"with the threshold at 4, Z's 3 bookmarks shouldn't save it : dropped [{string.Join(",", dropped.IsolatedSystems)}]");
        }

        private static void CheckTwoOptOnAsymmetricCost()
        {
            int[,] cost = new int[4, 4];
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    cost[i, j] = i == j ? 0 : 50;
                }
            }

            cost[0, 1] = 10;
            cost[0, 2] = 1;
            cost[1, 2] = 1;
            cost[1, 3] = 1;
            cost[2, 1] = 100; // the asymmetry : 1 going 1->2, but 100 coming back
            cost[2, 3] = 10;

            List<int> tour = new List<int> { 0, 1, 2, 3 };

            long initial = BookmarkRouteSolver.TourCost(cost, tour);
            Assert(initial == 21, $"setup error: expected the starting tour to cost 21, got {initial}");

            BookmarkRouteSolver.TwoOptImprove(cost, tour);

            long final = BookmarkRouteSolver.TourCost(cost, tour);
            Assert(
                final <= initial,
                $"2-opt made the tour worse: {initial} -> {final} (it accepted a swap without measuring the interior edge it reversed)");
        }

        private static void CheckParserColumnPriority()
        {
            HashSet<string> knownSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Jita", "M-HU4V" };
            string Resolve(string s) => knownSystems.Contains(s) ? s : null;

            // 9-column real-format line where the *name* field (index 0) is itself a valid system name
            // ("Jita") -- a decoy that must lose to the real column-3 system.
            string line = "Jita\t坐标\t3\tM-HU4V\t0C-PZ4\t血脉\t2026.08.20 19:02\t-\tBaiweia";

            ParseResult result = BookmarkParser.Parse(line, Resolve);

            Assert(result.UnparsedLines.Count == 0, "expected the decoy line to parse successfully");
            Assert(
                result.BookmarkCounts.ContainsKey("M-HU4V") && !result.BookmarkCounts.ContainsKey("Jita"),
                "expected the column-3 system (M-HU4V) to win over the name-column decoy (Jita)");
        }
    }
}
