using NUnit.Framework;
using Starsoil.BalanceSim.Scenarios;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>
    /// M4 acceptance: launch → land → survive on the new world → orbital resupply →
    /// return switch, with the home region healthy under the abstract model throughout.
    /// Runs in CI gate 1.
    /// </summary>
    public sealed class E4ScenarioTests
    {
        [Test]
        public void E4_LaunchColonize_Resupply_ReturnIntact()
        {
            var universe = E4Scenario.Build();
            int padId = E4Scenario.PadId(universe);
            Assert.AreNotEqual(0, padId);

            universe.ActiveWorld.Commands.Enqueue(new SetPadOrderCommand
            {
                PadId = padId,
                TargetBodyId = "palewatch",
                Payload = Universe.ColonistPodPayload,
                Crew = 2,
                Cargo = E4Scenario.LanderCargo()
            });

            // Logistics hauls parts from storage onto the pad; the rocket lifts at a window.
            bool launched = false;
            for (int i = 0; i < GameConstants.TicksPerDay * 3 && !launched; i++)
            {
                universe.Step();
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is RocketLaunchedEvent)
                    {
                        launched = true;
                    }
                }
            }
            Assert.IsTrue(launched, "logistics must assemble the rocket and launch within 3 days");

            bool arrived = false;
            for (int i = 0; i < GameConstants.TicksPerDay * 2 && !arrived; i++)
            {
                universe.Step();
                arrived = universe.FrozenRegions.Count > 0;
            }
            Assert.IsTrue(arrived, "colonist pod must open the Palewatch region");
            int newRegion = 0;
            foreach (int id in universe.FrozenRegions.Keys)
            {
                newRegion = id;
            }

            // Live on Palewatch for 10 days; the home base runs abstractly.
            universe.SwitchActive(newRegion);
            Assert.AreEqual(2, universe.ActiveWorld.Colonists.AliveCount, "crew must land alive");
            long target = universe.ActiveWorld.Tick + 10L * GameConstants.TicksPerDay;
            bool resupplyQueued = false;
            while (universe.ActiveWorld.Tick < target)
            {
                universe.Step();
                if (!resupplyQueued && universe.ActiveWorld.Tick >= target - 5L * GameConstants.TicksPerDay)
                {
                    universe.QueueCargoTransit(newRegion, "palewatch",
                        new System.Collections.Generic.List<Ingredient>
                        {
                            new Ingredient { ItemId = ItemIds.Water, Count = 20 },
                            new Ingredient { ItemId = ItemIds.Ration, Count = 20 }
                        });
                    resupplyQueued = true;
                }
            }
            Assert.AreEqual(2, universe.ActiveWorld.Colonists.AliveCount,
                "both settlers must survive 10 days on Palewatch");
            Assert.GreaterOrEqual(universe.ActiveWorld.CountItemEverywhere(ItemIds.Water), 20,
                "orbital resupply must have landed (cargo to the active region)");

            // Home region under the abstract model: no casualties, food/water held.
            var home = universe.FrozenRegions[1];
            Assert.AreEqual(0, home.PendingDeaths, "home region must not lose anyone while abstract");
            Assert.IsTrue(home.Stockpile.TryGetValue(ItemIds.Water, out int homeWater) && homeWater > 0,
                "home water must hold under derived production rates");

            universe.SwitchActive(1);
            Assert.AreEqual("dustloam", universe.ActiveBodyId);
            Assert.Greater(universe.ActiveWorld.Colonists.AliveCount, 0, "home crew must be alive on return");
            Assert.AreEqual(1, universe.FrozenRegions.Count, "Palewatch freezes back");
        }

        [Test]
        public void E4_IsDeterministic_ThroughLaunchAndLanding()
        {
            Assert.AreEqual(RunFingerprint(), RunFingerprint(),
                "universe flow must replay identically (docs/plan/08 determinism)");
        }

        private static string RunFingerprint()
        {
            var universe = E4Scenario.Build();
            int padId = E4Scenario.PadId(universe);
            universe.ActiveWorld.Commands.Enqueue(new SetPadOrderCommand
            {
                PadId = padId,
                TargetBodyId = "palewatch",
                Payload = Universe.ColonistPodPayload,
                Crew = 2,
                Cargo = E4Scenario.LanderCargo()
            });
            for (int i = 0; i < GameConstants.TicksPerDay * 4; i++)
            {
                universe.Step();
            }
            ulong hash = universe.ActiveWorld.ComputeStateHash();
            string slots = "";
            foreach (var pair in universe.FrozenRegions)
            {
                slots += pair.Key + ":" + pair.Value.BodyId + ":" + pair.Value.ColonistCount + ";";
            }
            return hash + "|" + slots + "|" + universe.Transits.Count;
        }
    }
}
