using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M2-T7/T8/T11: job matrix P0 bans, strikes, research loop and tech gating.</summary>
    public sealed class JobsTechTests
    {
        [Test]
        public void P0_Job_NeverTakesForbiddenTask()
        {
            var world = TestUtil.NewColonyWorld(101UL, 96);
            // Everyone becomes a Researcher; the default matrix forbids Mine (P0) for them.
            world.Commands.Enqueue(new SetJobQuotasCommand
            {
                Quotas = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>
                {
                    new System.Collections.Generic.KeyValuePair<string, int>("Researcher", 4)
                }
            });
            foreach (var node in world.Nodes.All)
            {
                node.Value.Designated = true;
            }
            TestUtil.Run(world, Balance.DispatchIntervalTicks * 6);

            foreach (var colonist in world.Colonists.AllSorted())
            {
                Assert.AreEqual(JobType.Researcher, colonist.Job);
                if (colonist.TaskId != 0 && world.Tasks.TryGet(colonist.TaskId, out var task))
                {
                    Assert.AreNotEqual(TaskType.Mine, task.Type,
                        "P0 (forbidden) task type was dispatched to a researcher (M2-T8)");
                }
            }
        }

        [Test]
        public void LowMorale_TriggersStrike_AtDayRollover()
        {
            var world = TestUtil.NewColonyWorld(102UL, 96);
            var colonist = world.Colonists.AllSorted()[0];
            colonist.MoraleEventOffset = -60f;
            bool struck = TestUtil.RunUntil(world, 2 * GameConstants.TicksPerDay,
                w => colonist.OnStrike);
            Assert.IsTrue(struck, "morale ≤20 must trigger a strike at day rollover (docs/plan/02)");

            foreach (var node in world.Nodes.All)
            {
                node.Value.Designated = true;
            }
            TestUtil.Run(world, Balance.DispatchIntervalTicks * 3);
            Assert.AreEqual(0, colonist.TaskId, "striking colonists refuse assignments");
        }

        [Test]
        public void Research_UnlocksBuilding_EndToEnd()
        {
            var world = TestUtil.NewColonyWorld(103UL, 96);
            int bench = world.Buildings.Place(BuildingDefs.ResearchBenchId, world.StartX + 4, world.StartY, 0, out _);
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add(ItemIds.SurveyDataCore, 3);

            // Locked before research: solar blueprint must be rejected.
            world.Commands.Enqueue(new PlaceBlueprintCommand
            {
                DefId = BuildingDefs.SolarPanelId,
                X = world.StartX - 6,
                Y = world.StartY - 6,
                Rotation = 0
            });
            world.Step();
            bool rejected = false;
            foreach (var evt in world.Events)
            {
                if (evt is CommandRejectedEvent r && r.Reason.Contains("Locked"))
                {
                    rejected = true;
                }
            }
            Assert.IsTrue(rejected, "locked building must reject blueprints (M2-T11 gating)");
            Assert.AreEqual(0, world.Blueprints.All.Count);

            world.Commands.Enqueue(new SetResearchTargetCommand { NodeId = "power_basics" });
            bool unlocked = TestUtil.RunUntil(world, 30000, w => w.Tech.IsUnlocked("power_basics"));
            Assert.IsTrue(unlocked, "research loop (haul cores → bench work → unlock) never completed");

            world.Commands.Enqueue(new PlaceBlueprintCommand
            {
                DefId = BuildingDefs.SolarPanelId,
                X = world.StartX - 6,
                Y = world.StartY - 6,
                Rotation = 0
            });
            world.Step();
            Assert.AreEqual(1, world.Blueprints.All.Count, "unlocked building must be placeable");
        }

        [Test]
        public void ResearchProgress_SurvivesSaveLoad()
        {
            var world = TestUtil.NewColonyWorld(104UL, 96);
            world.Buildings.Place(BuildingDefs.ResearchBenchId, world.StartX + 4, world.StartY, 0, out _);
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add(ItemIds.SurveyDataCore, 3);
            world.Commands.Enqueue(new SetResearchTargetCommand { NodeId = "power_basics" });
            TestUtil.RunUntil(world, 30000, w => w.Tech.IsUnlocked("power_basics"));
            Assert.IsTrue(world.Tech.IsUnlocked("power_basics"));

            var restored = SaveSerializer.Restore(
                SaveSerializer.FromGzipJson(SaveSerializer.ToGzipJson(SaveSerializer.Capture(world))));
            TestUtil.LoadTempRecipes(restored);
            TestUtil.LoadTechTree(restored);
            Assert.IsTrue(restored.Tech.IsUnlocked("power_basics"), "unlocked tech must persist through saves");
            Assert.IsTrue(restored.Tech.IsUnlocked("t0_survival"), "zero-cost nodes stay unlocked after load");
        }

        [Test]
        public void TechTree_HasAtLeastTwentyNodes()
        {
            var world = TestUtil.NewColonyWorld(105UL, 96);
            Assert.GreaterOrEqual(world.Tech.Nodes.Count, 20, "M2-T11: T0-T2 tree must define ≥20 nodes");
        }
    }
}
