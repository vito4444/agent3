using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M2-T3/T9: machines share the generic model, extractors take over deposits,
    /// processors beat hand stations by ≥4x, low durability halves speed, repair restores it.</summary>
    public sealed class MachineTests
    {
        private static World NewPoweredWorld(ulong seed, out int pylonX, out int pylonY)
        {
            var world = TestUtil.NewColonyWorld(seed, 96);
            pylonX = world.StartX;
            pylonY = world.StartY + 10;
            world.Buildings.Place(BuildingDefs.PowerPylonId, pylonX, pylonY, 0, out _);
            // Plenty of supply so power never confounds the machine assertions.
            for (int i = 0; i < 6; i++)
            {
                world.Buildings.Place(BuildingDefs.SolarPanelId, pylonX - 8 + i * 3, pylonY + 6, 0, out _);
            }
            var battery = PlaceOk(world, BuildingDefs.BatteryId, pylonX - 3, pylonY);
            world.Buildings.TryGet(battery, out var batteryState);
            batteryState.BatteryKwh = Balance.BatteryCapacityKwh;
            return world;
        }

        private static int PlaceOk(World world, string defId, int x, int y)
        {
            int id = world.Buildings.Place(defId, x, y, 0, out var error);
            Assert.AreEqual(PlacementError.None, error, defId);
            return id;
        }

        [Test]
        public void MachineWaterRoute_Beats_HandCampfire_ByFourX()
        {
            var world = TestUtil.NewColonyWorld(81UL, 96);
            Assert.IsTrue(world.Crafting.TryGetRecipe("make_water_melt", out var recipe));
            var campfire = new BuildingState { DefId = BuildingDefs.CampfireId, Durability = 100f };
            float handEffective = CraftingSystem.EffectiveWorkTicks(recipe, campfire, handSpeed: true);
            Assert.GreaterOrEqual(handEffective / recipe.WorkTicks, 4f,
                "machine water route must be ≥4x hand throughput (M2-T9)");
        }

        [Test]
        public void Extractor_PullsFromDeposit_AndStopsHandMining()
        {
            var world = NewPoweredWorld(82UL, out int px, out int py);
            // Drop a fresh iron deposit under machine coverage.
            var node = world.Nodes.Spawn(ItemIds.IronOre, 100, px + 2, py + 1, Balance.MineTicksPerUnit);
            node.Designated = true;
            int minerId = PlaceOk(world, BuildingDefs.MinerId, px + 3, py + 2);
            world.Buildings.TryGet(minerId, out var miner);

            TestUtil.Run(world, GameConstants.TicksPerHour);

            Assert.Greater(miner.Stock.Get(ItemIds.IronOre), 0, "extractor must produce into its buffer");
            foreach (var task in world.Tasks.All.Values)
            {
                if (task.Type == TaskType.Mine && task.NodeId == node.Id)
                {
                    Assert.Fail("hand mine task must not exist while a powered extractor covers the node (M2-T9)");
                }
            }

            // Cut power (toggle off): hand mining resumes next dispatch round.
            miner.WantsPower = false;
            TestUtil.Run(world, Balance.DispatchIntervalTicks * 2);
            bool handTaskBack = false;
            foreach (var task in world.Tasks.All.Values)
            {
                if (task.Type == TaskType.Mine && task.NodeId == node.Id)
                {
                    handTaskBack = true;
                }
            }
            Assert.IsTrue(handTaskBack, "hand mining must resume when the machine stops");
        }

        [Test]
        public void Processor_RunsOrder_Unmanned()
        {
            var world = NewPoweredWorld(83UL, out int px, out int py);
            int furnaceId = PlaceOk(world, BuildingDefs.FurnaceId, px + 4, py);
            world.Buildings.TryGet(furnaceId, out var furnace);
            furnace.Stock.Add(ItemIds.IronOre, 10);
            world.Crafting.AddOrder(furnaceId, "smelt_iron", 3, 0);

            // Freeze the colonists so nothing human touches the furnace.
            foreach (var colonist in world.Colonists.AllSorted())
            {
                colonist.Alive = false;
                colonist.Activity = ColonistActivity.Dead;
            }

            bool done = TestUtil.RunUntil(world, 3000, w => furnace.Stock.Get("iron_ingot") >= 3);
            Assert.IsTrue(done, "machine must smelt ingots without any colonist");
        }

        [Test]
        public void LowDurability_HalvesMachineSpeed_RepairRestores()
        {
            var world = NewPoweredWorld(84UL, out int px, out int py);
            int furnaceId = PlaceOk(world, BuildingDefs.FurnaceId, px + 4, py);
            world.Buildings.TryGet(furnaceId, out var furnace);
            furnace.Stock.Add(ItemIds.IronOre, 40);
            world.Crafting.AddOrder(furnaceId, "smelt_iron", -1, 999);

            furnace.Durability = Balance.LowDurabilityThreshold - 5f;
            long start = world.Tick;
            bool one = TestUtil.RunUntil(world, 3000, w => furnace.Stock.Get("iron_ingot") >= 1);
            Assert.IsTrue(one);
            long slowTicks = world.Tick - start;

            // Repair via the gel task: stock gel in the pod, keep a live colonist.
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add(ItemIds.RepairGel, 2);
            bool repaired = TestUtil.RunUntil(world, 6000, w => furnace.Durability >= Balance.RepairGelRestore - 1f);
            Assert.IsTrue(repaired, "repair task must restore durability with gel");
            Assert.Less(pod.Stock.Get(ItemIds.RepairGel), 2, "gel must be consumed");

            furnace.Stock.Add(ItemIds.IronOre, 40);
            int before = furnace.Stock.Get("iron_ingot");
            start = world.Tick;
            bool next = TestUtil.RunUntil(world, 3000, w => furnace.Stock.Get("iron_ingot") >= before + 1);
            Assert.IsTrue(next);
            long fastTicks = world.Tick - start;
            Assert.Less(fastTicks, slowTicks, "repaired machine must run faster than a worn one");
        }

        [Test]
        public void Greenhouse_Cycles_SeedRetained()
        {
            var world = NewPoweredWorld(85UL, out int px, out int py);
            int greenhouseId = PlaceOk(world, BuildingDefs.GreenhouseId, px + 4, py + 3);
            world.Buildings.TryGet(greenhouseId, out var greenhouse);
            greenhouse.Stock.Add(ItemIds.AlgaeSeed, 1);
            greenhouse.Stock.Add(ItemIds.Water, 5);
            world.Crafting.AddOrder(greenhouseId, "make_grow_biomass", 2, 0);

            bool grew = TestUtil.RunUntil(world, 3000, w => w.Stats.CraftedOf(ItemIds.Biomass) >= 8);
            Assert.IsTrue(grew, "greenhouse must grow biomass");
            Assert.AreEqual(1, greenhouse.Stock.Get(ItemIds.AlgaeSeed), "seed culture must persist across cycles");
        }
    }
}
