using System;
using System.Collections.Generic;
using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M1-T2: A* correctness vs a Dijkstra reference, road speed, multi-agent roam.</summary>
    public sealed class PathfindingTests
    {
        private const int CaseCount = 50;

        [Test]
        public void AStar_MatchesDijkstraCost_On50Cases()
        {
            var world = new World(11UL, 96);
            var ctx = world.PathContext;
            var rng = Rng.CreateStream(99UL, "pathcases");
            int solved = 0;
            int attempts = 0;
            while (solved < CaseCount && attempts < CaseCount * 10)
            {
                attempts++;
                int sx = rng.NextInt(0, 96);
                int sy = rng.NextInt(0, 96);
                int gx = rng.NextInt(0, 96);
                int gy = rng.NextInt(0, 96);
                if (!ctx.IsWalkable(sx, sy) || !ctx.IsWalkable(gx, gy))
                {
                    continue;
                }
                var path = Pathfinding.FindPath(ctx, sx, sy, gx, gy);
                int reference = DijkstraCost(ctx, sx, sy, gx, gy);
                if (path == null)
                {
                    Assert.AreEqual(int.MaxValue, reference, "A* said unreachable but Dijkstra found a path");
                    continue;
                }
                Assert.AreEqual(reference, PathCost(ctx, path), "cost mismatch case " + solved);
                solved++;
            }
            Assert.GreaterOrEqual(solved, CaseCount, "not enough solvable cases generated");
        }

        private static int PathCost(Pathfinding.Context ctx, List<(int x, int y)> path)
        {
            int cost = 0;
            for (int i = 1; i < path.Count; i++)
            {
                cost += ctx.StepCost(path[i - 1].x, path[i - 1].y, path[i].x, path[i].y);
            }
            return cost;
        }

        private static int DijkstraCost(Pathfinding.Context ctx, int sx, int sy, int gx, int gy)
        {
            int size = ctx.Terrain.Size;
            var dist = new Dictionary<int, int> { [sy * size + sx] = 0 };
            var visited = new HashSet<int>();
            var frontier = new SortedSet<(int cost, int key)>();
            frontier.Add((0, sy * size + sx));
            var deltas = new (int dx, int dy)[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

            while (frontier.Count > 0)
            {
                var current = frontier.Min;
                frontier.Remove(current);
                if (visited.Contains(current.key))
                {
                    continue;
                }
                visited.Add(current.key);
                int cx = current.key % size;
                int cy = current.key / size;
                if (cx == gx && cy == gy)
                {
                    return current.cost;
                }
                foreach (var (dx, dy) in deltas)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (!ctx.CanStep(cx, cy, nx, ny))
                    {
                        continue;
                    }
                    int key = ny * size + nx;
                    int nd = current.cost + ctx.StepCost(cx, cy, nx, ny);
                    if (!dist.TryGetValue(key, out int existing) || nd < existing)
                    {
                        dist[key] = nd;
                        frontier.Add((nd, key));
                    }
                }
            }
            return int.MaxValue;
        }

        [Test]
        public void RoadCells_ReduceStepCost_ByRoadFactor()
        {
            var world = new World(12UL, 96);
            int y = world.StartY + 8;
            for (int x = world.StartX - 5; x <= world.StartX + 5; x++)
            {
                world.Buildings.Place(BuildingDefs.RoadId, x, y, 0, out var err);
                Assert.AreEqual(PlacementError.None, err);
            }
            var ctx = world.PathContext;
            Assert.AreEqual(10, ctx.StepCost(world.StartX, y + 2, world.StartX + 1, y + 2), "plain step cost");
            Assert.AreEqual(7, ctx.StepCost(world.StartX, y, world.StartX + 1, y), "road step cost (10/1.4)");
        }

        [Test]
        public void RoadTravel_IsAboutFortyPercentFaster()
        {
            // Two identical worlds; one gets a road along the mining route (M1-T2 timing assert).
            long plain = TravelTicks(withRoad: false);
            long road = TravelTicks(withRoad: true);
            float ratio = plain / (float)road;
            Assert.That(ratio, Is.InRange(1.2f, 1.6f),
                "expected ≈1.4 road speedup, got " + ratio + " (plain " + plain + " vs road " + road + ")");
        }

        private static long TravelTicks(bool withRoad)
        {
            var world = new World(13UL, 96);
            // A single colonist far from a designated node walks to it; measure arrival tick.
            int targetX = world.StartX + 30;
            int targetY = world.StartY;
            var node = world.Nodes.Spawn(ItemIds.Ice, 50, targetX, targetY, Balance.MineTicksPerUnit);
            node.Designated = true;
            if (withRoad)
            {
                for (int x = world.StartX - 2; x < targetX; x++)
                {
                    world.Buildings.Place(BuildingDefs.RoadId, x, targetY, 0, out _);
                }
            }
            // Remove the other colonists so exactly one runs (deterministic single agent).
            var sorted = world.Colonists.AllSorted();
            for (int i = 1; i < sorted.Count; i++)
            {
                sorted[i].Alive = false;
                sorted[i].Activity = ColonistActivity.Dead;
            }
            var walker = sorted[0];
            long start = world.Tick;
            bool arrived = TestUtil.RunUntil(world, 6000,
                w => Math.Abs(walker.X - targetX) + Math.Abs(walker.Y - targetY) <= 1);
            Assert.IsTrue(arrived, "colonist never reached the node (road=" + withRoad + ")");
            return world.Tick - start;
        }

        [Test]
        public void SixtyColonists_RoamTenRealMinutes_NoDeadlock()
        {
            var world = TestUtil.NewColonyWorld(14UL, 192);
            for (int i = world.Colonists.All.Count; i < 60; i++)
            {
                world.Colonists.Spawn(world.PodInteriorX, world.PodInteriorY);
            }
            // This is a pathfinding/dispatch stress test, not an O2-economy test: the pod
            // life support is sized for a starting crew, not 60 people, so give everyone
            // oversized bottles to keep suffocation out of the picture.
            foreach (var colonist in world.Colonists.AllSorted())
            {
                colonist.BottleO2 = 100000f;
            }
            foreach (var node in world.Nodes.All)
            {
                node.Value.Designated = true;
            }
            const int tenRealMinutesAt1x = 6000;
            TestUtil.Run(world, tenRealMinutesAt1x);

            Assert.AreEqual(60, world.Colonists.AliveCount, "colonists died during the roam window");
            int totalMined = 0;
            foreach (var pair in world.Stats.Mined)
            {
                totalMined += pair.Value;
            }
            Assert.GreaterOrEqual(totalMined, 60, "the crowd barely worked; likely a dispatch or path deadlock");
            foreach (var colonist in world.Colonists.AllSorted())
            {
                if (colonist.TaskId != 0)
                {
                    Assert.IsTrue(world.Tasks.TryGet(colonist.TaskId, out _),
                        "colonist " + colonist.Id + " holds a dangling task id");
                }
            }
        }
    }
}
