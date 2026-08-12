using System.Diagnostics;
using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M1-T3: dispatch performance and correctness (priority order, single claim).</summary>
    public sealed class DispatchTests
    {
        [Test]
        public void TwoHundredTasks_SixtyWorkers_SingleRound_Under2ms()
        {
            var world = TestUtil.NewColonyWorld(31UL, 192);
            for (int i = world.Colonists.All.Count; i < 60; i++)
            {
                world.Colonists.Spawn(world.PodInteriorX + (i % 3) - 1, world.PodInteriorY + (i / 3) % 3 - 1);
            }
            // 200 loose piles + a storage building → 200 haul-to-storage candidates.
            world.Buildings.Place(BuildingDefs.SmallStorageId, world.StartX + 6, world.StartY + 6, 0, out _);
            var spread = Rng.CreateStream(5UL, "dispatch_bench");
            for (int i = 0; i < 200; i++)
            {
                world.Piles.Drop(ItemIds.IronOre, 1,
                    world.StartX - 20 + spread.NextInt(0, 40),
                    world.StartY - 20 + spread.NextInt(0, 40));
            }

            // Warm up JIT and task structures, then measure a fresh generation+dispatch round.
            world.Tasks.GenerateAndDispatch(world);
            var sw = new Stopwatch();
            double bestMs = double.MaxValue;
            for (int round = 0; round < 5; round++)
            {
                sw.Restart();
                world.Tasks.GenerateAndDispatch(world);
                sw.Stop();
                if (sw.Elapsed.TotalMilliseconds < bestMs)
                {
                    bestMs = sw.Elapsed.TotalMilliseconds;
                }
            }
            Assert.LessOrEqual(bestMs, 2.0, "dispatch round took " + bestMs + "ms (budget 2ms, M1-T3)");
        }

        [Test]
        public void HigherPriorityTask_IsAssignedFirst()
        {
            var world = TestUtil.NewColonyWorld(32UL, 96);
            // Keep exactly one worker.
            var sorted = world.Colonists.AllSorted();
            for (int i = 1; i < sorted.Count; i++)
            {
                sorted[i].Alive = false;
                sorted[i].Activity = ColonistActivity.Dead;
            }
            var worker = sorted[0];

            // Low-priority option: loose pile + storage. High-priority option: blueprint haul.
            world.Buildings.Place(BuildingDefs.SmallStorageId, world.StartX + 5, world.StartY + 5, 0, out _);
            world.Piles.Drop(ItemIds.Carbon, 4, world.StartX - 4, world.StartY - 4);
            world.Commands.Enqueue(new PlaceBlueprintCommand
            {
                DefId = BuildingDefs.CampfireId,
                X = world.StartX + 3,
                Y = world.StartY - 3,
                Rotation = 0
            });
            world.Step();
            // Force a dispatch round now.
            world.Tasks.GenerateAndDispatch(world);

            Assert.AreNotEqual(0, worker.TaskId, "worker got no task");
            Assert.IsTrue(world.Tasks.TryGet(worker.TaskId, out var task));
            Assert.AreEqual(TaskType.HaulToBlueprint, task.Type,
                "blueprint haul (priority " + TaskSystem.PriorityHaulBlueprint + ") must beat storage haul (" +
                TaskSystem.PriorityHaulStorage + ")");
        }

        [Test]
        public void Task_IsClaimedByExactlyOneColonist()
        {
            var world = TestUtil.NewColonyWorld(33UL, 96);
            foreach (var node in world.Nodes.All)
            {
                node.Value.Designated = true;
                break;
            }
            TestUtil.Run(world, Balance.DispatchIntervalTicks + 1);

            var claims = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var colonist in world.Colonists.AllSorted())
            {
                if (colonist.TaskId != 0)
                {
                    claims.TryGetValue(colonist.TaskId, out int count);
                    claims[colonist.TaskId] = count + 1;
                }
            }
            foreach (var pair in claims)
            {
                Assert.AreEqual(1, pair.Value, "task " + pair.Key + " claimed by " + pair.Value + " colonists");
            }
            foreach (var task in world.Tasks.All.Values)
            {
                if (task.ClaimedBy != 0)
                {
                    Assert.IsTrue(claims.ContainsKey(task.Id), "claimed task without a matching colonist");
                }
            }
        }
    }
}
