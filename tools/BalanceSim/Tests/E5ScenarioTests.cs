using NUnit.Framework;
using Starsoil.BalanceSim.Scenarios;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M5-T9 acceptance: the Dustloam↔Palewatch two-way line runs five game days
    /// hands-off; both ends hold their thresholds and no route suspends.</summary>
    public sealed class E5ScenarioTests
    {
        [Test]
        public void E5_TwoWayRoute_FiveDays_HandsOff()
        {
            var universe = E5Scenario.Build();
            int suspensions = 0;
            for (long i = 0; i < 5L * GameConstants.TicksPerDay; i++)
            {
                universe.Step();
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is RouteSuspendedEvent)
                    {
                        suspensions++;
                    }
                }
            }

            Assert.AreEqual(0, suspensions, "no route may suspend during the 5-day window (M5-T9)");
            var palewatch = universe.FrozenRegions[E5Scenario.PalewatchRegionId];
            palewatch.Stockpile.TryGetValue("steel_plate", out int steelAtOutpost);
            Assert.GreaterOrEqual(steelAtOutpost, 12,
                "Palewatch must hold its steel-plate threshold (outbound leg)");
            Assert.GreaterOrEqual(universe.ActiveWorld.CountItemEverywhere("bauxite"), 16,
                "Dustloam must hold its bauxite threshold (return leg)");
            Assert.Less(universe.ActiveWorld.CountItemEverywhere("rocket_fuel"), 10,
                "outbound flights must consume fuel");
            Assert.AreEqual(0, palewatch.PendingDeaths, "outpost crew must be fine throughout");
        }
    }
}
