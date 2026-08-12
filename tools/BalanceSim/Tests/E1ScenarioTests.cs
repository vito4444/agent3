using NUnit.Framework;
using Starsoil.BalanceSim.Scenarios;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>
    /// M1-T12 acceptance: the scripted hands-only opening survives 10 game days on
    /// standard difficulty with at least 2 of the 4 colonists alive (losses allowed,
    /// wipe not allowed). Runs in CI gate 1.
    /// </summary>
    public sealed class E1ScenarioTests
    {
        [Test]
        public void E1_TenDays_AtLeastTwoSurvive()
        {
            var world = E1Scenario.RunWithUpkeep(E1Scenario.DefaultSeed);
            Assert.GreaterOrEqual(world.Colonists.AliveCount, 2,
                "E1 survival baseline failed: colony effectively wiped. Deaths: " + DeathSummary(world));
            Assert.IsFalse(world.Defeated, "defeat flag must not be set while colonists live");
            Assert.GreaterOrEqual(world.Stats.CraftedOf(ItemIds.Water), 4, "water production never came online");
            Assert.GreaterOrEqual(world.Stats.CraftedOf(ItemIds.Ration), 1, "food production never came online");
        }

        [Test]
        public void E1_TwoDays_IsDeterministic()
        {
            ulong seed = E1Scenario.DefaultSeed;
            Assert.AreEqual(RunTwoDays(seed), RunTwoDays(seed),
                "full-colony sim diverged between identical runs (determinism, docs/plan/08)");
        }

        private static ulong RunTwoDays(ulong seed)
        {
            var world = TestUtil.NewColonyWorld(seed, E1Scenario.RegionSize);
            E1Scenario.ApplyOpeningScript(world);
            long target = 2L * GameConstants.TicksPerDay;
            while (world.Tick < target)
            {
                if (world.Tick % GameConstants.TicksPerHour == 0)
                {
                    E1Scenario.TickOrders(world);
                }
                world.Step();
            }
            return world.ComputeStateHash();
        }

        private static string DeathSummary(World world)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var pair in world.Stats.Deaths)
            {
                parts.Add(pair.Key + "x" + pair.Value);
            }
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }
    }
}
