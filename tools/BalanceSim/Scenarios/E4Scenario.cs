using System.Collections.Generic;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Scenarios
{
    /// <summary>
    /// E4 (docs/plan/09 M4): a steady home base assembles and launches a colonist pod to
    /// Palewatch, the new region survives on landed supplies while the home region runs
    /// the abstract model, an orbital resupply lands mid-stay, and the return switch
    /// finds the home base intact.
    /// </summary>
    public static class E4Scenario
    {
        public const ulong DefaultSeed = 4044UL;

        public static Universe Build()
        {
            var universe = TestUtil.NewUniverse(DefaultSeed, 96);
            var world = universe.ActiveWorld;
            world.Tech.UnlockAll();

            int cx = world.StartX;
            int cy = world.StartY + 8;

            // Power + production so the frozen home region derives positive rates
            // (E2-style water/food maintain lines).
            world.Buildings.Place(BuildingDefs.PowerPylonId, cx, cy, 0, out _);
            for (int i = 0; i < 8; i++)
            {
                world.Buildings.Place(BuildingDefs.SolarPanelId, cx - 12 + i * 3, cy + 5, 0, out _);
            }
            for (int i = 0; i < 3; i++)
            {
                int batteryId = world.Buildings.Place(BuildingDefs.BatteryId, cx - 6 + i * 2, cy - 5, 0, out _);
                world.Buildings.TryGet(batteryId, out var battery);
                battery.BatteryKwh = Balance.BatteryCapacityKwh;
            }
            var ice = world.Nodes.Spawn(ItemIds.Ice, 5000, cx - 8, cy + 9, Balance.MineTicksPerUnit);
            ice.Designated = false;
            world.Buildings.Place(BuildingDefs.IceMinerId, cx - 7, cy + 10, 0, out _);
            int purifier = world.Buildings.Place(BuildingDefs.WaterPurifierId, cx - 3, cy + 9, 0, out _);
            world.Crafting.AddOrder(purifier, "distill_water_clean", -1, 60);
            int greenhouse = world.Buildings.Place(BuildingDefs.GreenhouseId, cx, cy + 9, 0, out _);
            world.Buildings.TryGet(greenhouse, out var greenhouseState);
            greenhouseState.Stock.Add(ItemIds.AlgaeSeed, 1);
            world.Crafting.AddOrder(greenhouse, "make_grow_biomass", -1, 40);
            int press = world.Buildings.Place(BuildingDefs.PressId, cx + 4, cy + 9, 0, out _);
            world.Crafting.AddOrder(press, "make_ration", -1, 32);

            // Steady-state stocks.
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add(ItemIds.Water, 40);
            pod.Stock.Add(ItemIds.Ration, 24);
            pod.Stock.Add(ItemIds.Biomass, 40);

            // Launch infrastructure + rocket parts in storage (assembly hauling is the
            // point of this scenario: logistics must move them onto the pad).
            world.Buildings.Place(BuildingDefs.SmallStorageId, cx + 8, cy - 4, 0, out _);
            int storageId = world.Buildings.GetBuildingAt(cx + 8, cy - 4);
            world.Buildings.TryGet(storageId, out var storage);
            foreach (var part in Universe.RocketParts)
            {
                storage.Stock.Add(part.ItemId, part.Count);
            }
            storage.Stock.Add("colonist_pod", 1);
            storage.Stock.Add(ItemIds.Ration, 30);
            storage.Stock.Add(ItemIds.Water, 30);
            storage.Stock.Add(ItemIds.OxygenBottle, 4);

            world.Buildings.Place(BuildingDefs.LaunchPadId, cx + 12, cy - 6, 0, out _);

            universe.Step();
            return universe;
        }

        public static int PadId(Universe universe)
        {
            foreach (var pair in universe.ActiveWorld.Buildings.All)
            {
                if (pair.Value.DefId == BuildingDefs.LaunchPadId)
                {
                    return pair.Key;
                }
            }
            return 0;
        }

        public static List<Ingredient> LanderCargo()
        {
            return new List<Ingredient>
            {
                new Ingredient { ItemId = ItemIds.Ration, Count = 30 },
                new Ingredient { ItemId = ItemIds.Water, Count = 30 },
                new Ingredient { ItemId = ItemIds.OxygenBottle, Count = 4 }
            };
        }
    }
}
