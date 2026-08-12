using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M1-T5: hand crafting end to end — order, input hauling, work at 0.5× rate, output.</summary>
    public sealed class CraftingFlowTests
    {
        [Test]
        public void HandStations_RunSlower_ByTheMappingFactor()
        {
            var world = TestUtil.NewColonyWorld(41UL, 96);
            Assert.IsTrue(world.Crafting.TryGetRecipe("make_water_melt", out var recipe));
            Assert.IsTrue(recipe.RunsOn(BuildingKind.Campfire, out bool hand) && hand,
                "water melting must run on the campfire as a hand station");
            Assert.IsTrue(recipe.RunsOn(BuildingKind.Furnace, out bool machineHand) && !machineHand,
                "water melting must run on the furnace at machine speed");
            var station = new BuildingState { DefId = BuildingDefs.CampfireId, Durability = 100f };
            Assert.AreEqual(recipe.WorkTicks * Balance.HandcraftTimeFactor,
                CraftingSystem.EffectiveWorkTicks(recipe, station, handSpeed: true), 0.001f,
                "hand stations run at 1/" + Balance.HandcraftTimeFactor + " machine speed (docs/plan/03 mapping)");
        }

        [Test]
        public void LowDurability_HalvesWorkSpeed()
        {
            var world = TestUtil.NewColonyWorld(42UL, 96);
            Assert.IsTrue(world.Crafting.TryGetRecipe("make_water_melt", out var recipe));
            var worn = new BuildingState { DefId = BuildingDefs.CampfireId, Durability = Balance.LowDurabilityThreshold - 1f };
            var fresh = new BuildingState { DefId = BuildingDefs.CampfireId, Durability = 100f };
            Assert.AreEqual(
                CraftingSystem.EffectiveWorkTicks(recipe, fresh, true) / Balance.LowDurabilitySpeedFactor,
                CraftingSystem.EffectiveWorkTicks(recipe, worn, true), 0.001f);
        }

        [Test]
        public void MeltIceOrder_HauledCraftedAndCounted()
        {
            var world = TestUtil.NewColonyWorld(43UL, 96);
            int stationId = world.Buildings.Place(BuildingDefs.CampfireId, world.StartX + 4, world.StartY, 0, out var err);
            Assert.AreEqual(PlacementError.None, err);
            world.Piles.Drop(ItemIds.Ice, 4, world.StartX - 4, world.StartY);
            world.Commands.Enqueue(new AddCraftOrderCommand { StationId = stationId, RecipeId = "make_water_melt", Count = 1 });

            bool crafted = TestUtil.RunUntil(world, 6000, w => w.Stats.CraftedOf(ItemIds.Water) >= 2);
            Assert.IsTrue(crafted, "melt_ice order never completed");
            world.Buildings.TryGet(stationId, out var station);
            Assert.GreaterOrEqual(station.Stock.Get(ItemIds.Water), 2, "water must land in the station buffer");
            Assert.AreEqual(0, station.Stock.Get(ItemIds.Ice), "inputs must be consumed");
        }

        [Test]
        public void MaintainOrder_StopsAtTarget()
        {
            var world = TestUtil.NewColonyWorld(44UL, 96);
            int stationId = world.Buildings.Place(BuildingDefs.CampfireId, world.StartX + 4, world.StartY, 0, out _);
            world.Piles.Drop(ItemIds.Ice, 40, world.StartX - 4, world.StartY);
            // Target above the crash pod's starting water so the order actually runs.
            int startWater = world.CountItemEverywhere(ItemIds.Water);
            int target = startWater + 4;
            world.Commands.Enqueue(new AddCraftOrderCommand
            {
                StationId = stationId,
                RecipeId = "make_water_melt",
                Count = -1,
                MaintainTarget = target
            });

            TestUtil.RunUntil(world, 8000, w => w.CountItemEverywhere(ItemIds.Water) >= target);
            long settledAt = world.Tick;
            TestUtil.Run(world, 2000);
            int water = world.CountItemEverywhere(ItemIds.Water);
            Assert.That(water, Is.InRange(target, target + 2),
                "maintain order overshot: " + water + " water after settling at tick " + settledAt);
        }
    }
}
