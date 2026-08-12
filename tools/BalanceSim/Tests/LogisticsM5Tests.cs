using System.Collections.Generic;
using NUnit.Framework;
using Starsoil.BalanceSim.Scenarios;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M5: transfer times, landing-beacon cargo loss, outpost kits, automated
    /// two-way routes and the faction warning placeholder.</summary>
    public sealed class LogisticsM5Tests
    {
        [Test]
        public void TransferTimes_MatchPlanTable_ThreeSpotChecks()
        {
            var universe = TestUtil.NewUniverse(131UL, 96);
            // Same body: 1h.
            Assert.AreEqual(GameConstants.TicksPerHour,
                universe.TransferTicks("dustloam", "dustloam"), "same-body hop");
            // Adjacent orbit (dustloam 2 → flintfield 3): 2h.
            Assert.AreEqual(2L * GameConstants.TicksPerHour,
                universe.TransferTicks("dustloam", "flintfield"), "adjacent orbit");
            // dustloam 2 → sleetfall 8 = 6 hops: 2h + 5×4h = 22h.
            Assert.AreEqual(22L * GameConstants.TicksPerHour,
                universe.TransferTicks("dustloam", "sleetfall"), "far hop chain");
        }

        [Test]
        public void CargoLoss_FivePercentWithoutBeacon_ZeroWith()
        {
            // Without a beacon: 5% per stack, floored.
            var universe = TestUtil.NewUniverse(132UL, 96);
            universe.QueueCargoTransit(1, "dustloam",
                new List<Ingredient> { new Ingredient { ItemId = ItemIds.Water, Count = 40 } });
            int before = universe.ActiveWorld.CountItemEverywhere(ItemIds.Water);
            bool lossSeen = false;
            for (int i = 0; i < GameConstants.TicksPerHour * 2; i++)
            {
                universe.Step();
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is CargoLossEvent loss)
                    {
                        Assert.AreEqual(2, loss.UnitsLost, "5% of 40 = 2 units lost");
                        lossSeen = true;
                    }
                }
            }
            Assert.IsTrue(lossSeen, "no-beacon landing must lose cargo (M5-T5)");
            Assert.AreEqual(before + 38, universe.ActiveWorld.CountItemEverywhere(ItemIds.Water),
                "ledger: 38 of 40 delivered");

            // With a beacon: zero loss.
            var universeB = TestUtil.NewUniverse(133UL, 96);
            var world = universeB.ActiveWorld;
            world.Buildings.Place(BuildingDefs.LandingBeaconId, world.StartX + 5, world.StartY + 5, 0, out var err);
            Assert.AreEqual(PlacementError.None, err);
            int beforeB = world.CountItemEverywhere(ItemIds.Water);
            universeB.QueueCargoTransit(1, "dustloam",
                new List<Ingredient> { new Ingredient { ItemId = ItemIds.Water, Count = 40 } });
            for (int i = 0; i < GameConstants.TicksPerHour * 2; i++)
            {
                universeB.Step();
                foreach (var evt in universeB.ActiveWorld.Events)
                {
                    Assert.IsFalse(evt is CargoLossEvent, "beacon landing must be lossless (M5-T5)");
                }
            }
            Assert.AreEqual(beforeB + 40, world.CountItemEverywhere(ItemIds.Water), "ledger: all 40 delivered");
        }

        [Test]
        public void OutpostKit_ExpandsFourBuildings_AndSixSurviveTwoDays()
        {
            var cargo = new List<Ingredient>
            {
                new Ingredient { ItemId = "outpost_kit", Count = 1 }
            };
            var body = new BodyDef { Id = "palewatch", Zh = "苍卫", En = "Palewatch", Landable = true, NightCold = true, SolarFactor = 0.9f };
            body.Resources.Add(ItemIds.IronOre);
            var world = World.CreateLandingRegion(555UL, 96, body, 6, cargo);
            TestUtil.LoadTempRecipes(world);
            TestUtil.LoadTechTree(world);

            Assert.AreEqual(0, world.CountItemEverywhere("outpost_kit"), "kit must be consumed on deploy");
            // Pod + four micro-buildings.
            Assert.GreaterOrEqual(world.Buildings.Count, 5, "outpost kit must expand 4 buildings (M5-T6)");

            TestUtil.Run(world, 2 * GameConstants.TicksPerDay);
            Assert.AreEqual(6, world.Colonists.AliveCount,
                "6 settlers must survive 2 game days on the kit buffer without resupply (M5-T6)");
        }

        [Test]
        public void FactionHomeBodyLanding_RaisesWarningPlaceholder()
        {
            var universe = TestUtil.NewUniverse(134UL, 96);
            universe.QueueCargoTransit(0, "redridge", new List<Ingredient>());
            bool warned = false;
            for (int i = 0; i < GameConstants.TicksPerDay * 2; i++)
            {
                universe.Step();
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is FactionWarningEvent warning && warning.BodyId == "redridge")
                    {
                        warned = true;
                    }
                }
            }
            Assert.IsTrue(warned, "landing on a faction home body must raise the M6 placeholder warning");
        }

        [Test]
        public void RouteWithoutFuel_SuspendsWithAlert()
        {
            var universe = TestUtil.NewUniverse(135UL, 96);
            // A frozen destination that wants steel plates the active region does not have fuel to send.
            var slot = new RegionSlot { Id = 5, BodyId = "palewatch", Seed = 5UL, FrozenSave = new byte[0] };
            universe.FrozenRegions.Add(5, slot);
            universe.AddRoute(1, 5, "palewatch",
                new List<Ingredient> { new Ingredient { ItemId = "steel_plate", Count = 8 } });

            bool suspended = false;
            for (int i = 0; i < GameConstants.TicksPerHour * 3; i++)
            {
                universe.Step();
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is RouteSuspendedEvent s && s.Reason == "fuel")
                    {
                        suspended = true;
                    }
                }
            }
            Assert.IsTrue(suspended, "fuel-less route must suspend with an alert (M5-T7)");
        }
    }
}
