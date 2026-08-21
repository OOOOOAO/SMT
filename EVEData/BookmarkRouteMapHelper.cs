namespace SMT.EVEData
{
    /// <summary>
    /// Flattens a <see cref="BookmarkRoute"/> into a single ordered sequence of point-to-point edges for map
    /// drawing. Shared by RegionControl and UniverseControl so they don't each re-derive this.
    ///
    /// The one non-obvious part: the jump that connects one <see cref="RouteLine"/> to the next lives only as
    /// the next line's <see cref="RouteLine.EntryJumpLY"/> plus its first point -- it is never a two-point pair
    /// inside either line's <see cref="RouteLine.Points"/> (see BookmarkRouteSolver.BuildLines, which is on the
    /// do-not-modify list, so this re-stitches it from outside rather than changing what it emits).
    /// </summary>
    public static class BookmarkRouteMapHelper
    {
        public struct Edge
        {
            public string From;
            public string To;
            public bool IsJump;
            public decimal LY;
        }

        /// <param name="route">Result of a bookmark route calculation. Null/empty yields no edges.</param>
        /// <param name="startSystem">
        /// The system the whole route started from (character location at calculation time). Only used to draw
        /// the entry jump into the very first line, for the case where the route jumps straight out of the
        /// start with zero gate legs first -- that jump's origin isn't recorded anywhere in <paramref name="route"/>
        /// itself. Pass null/empty to skip that one edge.
        /// </param>
        public static IEnumerable<Edge> EnumerateEdges(BookmarkRoute route, string startSystem)
        {
            if (route == null)
            {
                yield break;
            }

            string prevLineEnd = string.IsNullOrEmpty(startSystem) ? null : startSystem;

            foreach (RouteLine line in route.Lines)
            {
                if (line.Points.Count == 0)
                {
                    continue;
                }

                if (line.EntryJumpLY > 0 && prevLineEnd != null)
                {
                    yield return new Edge { From = prevLineEnd, To = line.Points[0].SystemName, IsJump = true, LY = line.EntryJumpLY };
                }

                for (int i = 1; i < line.Points.Count; i++)
                {
                    yield return new Edge { From = line.Points[i - 1].SystemName, To = line.Points[i].SystemName, IsJump = false, LY = 0m };
                }

                prevLineEnd = line.Points[line.Points.Count - 1].SystemName;
            }
        }

        /// <summary>Assert-based self-check, run via <c>DataGen selfcheck</c> alongside <see cref="BookmarkRouteSelfCheck"/>.</summary>
        public static bool SelfCheck()
        {
            (string Name, Action Check)[] checks =
            {
                ("gate-only single line", CheckGateOnlyLine),
                ("entry jump stitched between two lines", CheckEntryJumpBetweenLines),
                ("a jump straight out of the start uses the supplied startSystem", CheckJumpFromStartEdge),
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

            return allPassed;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        private static void CheckGateOnlyLine()
        {
            RouteLine line = new RouteLine();
            line.Points.Add(new Navigation.RoutePoint { SystemName = "S", GateToTake = Navigation.GateType.StarGate });
            line.Points.Add(new Navigation.RoutePoint { SystemName = "A", GateToTake = Navigation.GateType.StarGate });
            line.Points.Add(new Navigation.RoutePoint { SystemName = "B", GateToTake = Navigation.GateType.StarGate });

            BookmarkRoute route = new BookmarkRoute();
            route.Lines.Add(line);

            List<Edge> edges = new List<Edge>(EnumerateEdges(route, "S"));

            Assert(edges.Count == 2, $"expected 2 gate edges, got {edges.Count}");
            Assert(!edges[0].IsJump && !edges[1].IsJump, "gate-only line must not produce jump edges");
            Assert(edges[0].From == "S" && edges[0].To == "A", "first edge should be S->A");
            Assert(edges[1].From == "A" && edges[1].To == "B", "second edge should be A->B");
        }

        private static void CheckEntryJumpBetweenLines()
        {
            RouteLine line1 = new RouteLine();
            line1.Points.Add(new Navigation.RoutePoint { SystemName = "S", GateToTake = Navigation.GateType.StarGate });
            line1.Points.Add(new Navigation.RoutePoint { SystemName = "A", GateToTake = Navigation.GateType.StarGate });

            RouteLine line2 = new RouteLine { EntryJumpLY = 5m };
            line2.Points.Add(new Navigation.RoutePoint { SystemName = "B", GateToTake = Navigation.GateType.JumpTo, LY = 5m });
            line2.Points.Add(new Navigation.RoutePoint { SystemName = "C", GateToTake = Navigation.GateType.StarGate });

            BookmarkRoute route = new BookmarkRoute();
            route.Lines.Add(line1);
            route.Lines.Add(line2);

            List<Edge> edges = new List<Edge>(EnumerateEdges(route, "S"));

            Assert(edges.Count == 3, $"expected 1 gate + 1 jump + 1 gate = 3 edges, got {edges.Count}");
            Assert(!edges[0].IsJump && edges[0].From == "S" && edges[0].To == "A", "edge 0 should be the gate leg S->A");
            Assert(edges[1].IsJump && edges[1].From == "A" && edges[1].To == "B" && edges[1].LY == 5m, "edge 1 should be the 5 LY jump A->B, stitched from line2.EntryJumpLY");
            Assert(!edges[2].IsJump && edges[2].From == "B" && edges[2].To == "C", "edge 2 should be the gate leg B->C");
        }

        private static void CheckJumpFromStartEdge()
        {
            // The start-only line was already dropped by BuildLines, so the surviving line 1 starts with
            // a JumpTo point and no earlier line to read a "from" off. EnumerateEdges must use startSystem instead.
            RouteLine line = new RouteLine { EntryJumpLY = 5m };
            line.Points.Add(new Navigation.RoutePoint { SystemName = "T1", GateToTake = Navigation.GateType.JumpTo, LY = 5m });

            BookmarkRoute route = new BookmarkRoute();
            route.Lines.Add(line);

            List<Edge> withStart = new List<Edge>(EnumerateEdges(route, "S"));
            Assert(withStart.Count == 1, $"expected exactly 1 edge, got {withStart.Count}");
            Assert(withStart[0].IsJump && withStart[0].From == "S" && withStart[0].To == "T1", "expected the S->T1 jump edge using the supplied start");

            List<Edge> withoutStart = new List<Edge>(EnumerateEdges(route, null));
            Assert(withoutStart.Count == 0, $"with no start system supplied, the entry jump can't be drawn and must be skipped, got {withoutStart.Count}");
        }
    }
}
