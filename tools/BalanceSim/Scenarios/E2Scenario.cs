using Starsoil.Core;

namespace Starsoil.BalanceSim.Scenarios
{
    /// <summary>
    /// E2 zero-manual-labor steady state (docs/plan/09 M2-T10): a pre-built powered base
    /// where machines extract and process, bots haul, and colonists are operators only.
    /// Run 24 game hours; oxygen, water, food and stored power must not shrink and no
    /// colonist may execute manual mine/haul/build labor.
    /// </summary>
    public static class E2Scenario
    {
        public const ulong DefaultSeed = 3033UL;
        public const int RegionSize = 96;

        public static World Build(ulong seed)
        {
            var world = TestUtil.NewColonyWorld(seed, RegionSize);
            world.Tech.UnlockAll();

            int cx = world.StartX;
            int cy = world.StartY + 8;

            // Power spine: pylons + generous solar + charged batteries (spaced grid).
            Place(world, BuildingDefs.PowerPylonId, cx, cy);
            Place(world, BuildingDefs.PowerPylonId, cx + 14, cy);
            Place(world, BuildingDefs.PowerPylonId, cx - 14, cy);
            for (int i = 0; i < 11; i++)
            {
                Place(world, BuildingDefs.SolarPanelId, cx - 15 + i * 3, cy + 5);
            }
            Place(world, BuildingDefs.WindTurbineId, cx - 14, cy - 3);
            Place(world, BuildingDefs.WindTurbineId, cx + 14, cy - 3);
            Place(world, BuildingDefs.WindTurbineId, cx + 16, cy - 3);
            for (int i = 0; i < 4; i++)
            {
                int id = Place(world, BuildingDefs.BatteryId, cx - 6 + i * 2, cy - 5);
                world.Buildings.TryGet(id, out var battery);
                battery.BatteryKwh = Balance.BatteryCapacityKwh;
            }

            // Oxygen: electrolyzer + tanks + charging station on a gas subnet that also
            // covers the crash pod (indoor breathing draws the network, not the standby tank).
            Place(world, BuildingDefs.GasPylonId, cx + 6, cy + 1);
            Place(world, BuildingDefs.GasPylonId, cx + 2, cy - 6);
            int electrolyzer = Place(world, BuildingDefs.ElectrolyzerId, cx + 8, cy + 1);
            Place(world, BuildingDefs.GasTankId, cx + 11, cy + 1);
            Place(world, BuildingDefs.GasTankId, cx + 14, cy + 2);
            Place(world, BuildingDefs.AirChargingStationId, cx + 5, cy - 1);
            world.Buildings.TryGet(electrolyzer, out var electrolyzerState);
            electrolyzerState.Stock.Add(ItemIds.Water, 10);

            // Water: a dedicated ice deposit with an ice extractor, purifier keeps stock.
            var iceNode = world.Nodes.Spawn(ItemIds.Ice, 5000, cx - 8, cy + 9, Balance.MineTicksPerUnit);
            iceNode.Designated = false;
            Place(world, BuildingDefs.IceMinerId, cx - 7, cy + 10);
            int purifier = Place(world, BuildingDefs.WaterPurifierId, cx - 3, cy + 9);
            world.Crafting.AddOrder(purifier, "distill_water_clean", -1, 60);

            // Food: greenhouses feed a press.
            int greenhouseA = Place(world, BuildingDefs.GreenhouseId, cx, cy + 9);
            int greenhouseB = Place(world, BuildingDefs.GreenhouseId, cx + 4, cy + 9);
            world.Buildings.TryGet(greenhouseA, out var gA);
            world.Buildings.TryGet(greenhouseB, out var gB);
            gA.Stock.Add(ItemIds.AlgaeSeed, 1);
            gB.Stock.Add(ItemIds.AlgaeSeed, 1);
            world.Crafting.AddOrder(greenhouseA, "make_grow_biomass", -1, 40);
            world.Crafting.AddOrder(greenhouseB, "make_grow_biomass", -1, 40);
            int press = Place(world, BuildingDefs.PressId, cx + 9, cy + 9);
            world.Crafting.AddOrder(press, "make_ration", -1, 32);

            // Logistics: bots haul, humans do not.
            Place(world, BuildingDefs.BotStationId, cx - 4, cy - 3);
            Place(world, BuildingDefs.ChargingPostId, cx - 1, cy - 3);
            Place(world, BuildingDefs.ChargingPostId, cx + 1, cy - 3);
            Place(world, BuildingDefs.SmallStorageId, cx - 9, cy + 1);

            // Colonists are operators only (docs/plan/09 M2-T10 wording).
            world.Commands.Enqueue(new SetJobQuotasCommand
            {
                Quotas = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>
                {
                    new System.Collections.Generic.KeyValuePair<string, int>("Operator", 4)
                }
            });
            // Steady-state stocks: maintain targets start satisfied so the measured day
            // reflects sustaining consumption, not one-off warehouse filling.
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add(ItemIds.Water, 40);
            pod.Stock.Add(ItemIds.Ration, 16);
            pod.Stock.Add(ItemIds.Biomass, 40);

            world.Step();

            // A pre-built base starts at steady state: the gas grid is mostly full, so the
            // electrolyzer duty-cycles instead of running a 24h tank-filling marathon.
            int component = world.Networks.GasComponentOf(electrolyzer);
            if (component != 0)
            {
                // Overshoot on purpose; the next settle clamps it to grid capacity.
                world.Networks.GasStored[component] = 100000f;
                world.Step();
            }
            return world;
        }

        private static int Place(World world, string defId, int x, int y)
        {
            int id = world.Buildings.Place(defId, x, y, 0, out var error);
            if (error != PlacementError.None)
            {
                throw new System.InvalidOperationException("E2 base layout failed: " + defId + " at " + x + "," + y + " → " + error);
            }
            return id;
        }
    }
}
