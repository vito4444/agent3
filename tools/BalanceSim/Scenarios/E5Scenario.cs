using System.Collections.Generic;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Scenarios
{
    /// <summary>
    /// E5 two-way route (docs/plan/09 M5-T9, docs/plan/06): Dustloam ships steel plates
    /// to Palewatch; the return leg brings bauxite home. Five game days hands-off, both
    /// ends must hold their stock thresholds.
    /// </summary>
    public static class E5Scenario
    {
        public const ulong DefaultSeed = 5055UL;
        public const int PalewatchRegionId = 2;

        public static Universe Build()
        {
            var universe = TestUtil.NewUniverse(DefaultSeed, 96);
            var world = universe.ActiveWorld;
            world.Tech.UnlockAll();

            int cx = world.StartX;
            int cy = world.StartY + 8;

            // Power for the steel line.
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

            // Steel plate line: iron + carbon extraction → smelt → alloy → roll.
            var iron = world.Nodes.Spawn(ItemIds.IronOre, 8000, cx - 8, cy + 9, Balance.MineTicksPerUnit);
            iron.Designated = false;
            world.Buildings.Place(BuildingDefs.MinerId, cx - 7, cy + 10, 0, out _);
            var carbon = world.Nodes.Spawn(ItemIds.Carbon, 8000, cx + 10, cy + 9, Balance.MineTicksPerUnit);
            carbon.Designated = false;
            world.Buildings.Place(BuildingDefs.MinerId, cx + 11, cy + 10, 0, out _);
            int furnace = world.Buildings.Place(BuildingDefs.FurnaceId, cx - 3, cy + 9, 0, out _);
            world.Crafting.AddOrder(furnace, "smelt_iron", -1, 16);
            int arc = world.Buildings.Place(BuildingDefs.ArcFurnaceId, cx, cy + 9, 0, out _);
            world.Crafting.AddOrder(arc, "alloy_steel", -1, 10);
            int mill = world.Buildings.Place(BuildingDefs.RollMillId, cx + 4, cy + 9, 0, out _);
            world.Crafting.AddOrder(mill, "form_steel_plate", -1, 24);

            // Household stocks + route fuel + lossless landings.
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add(ItemIds.Water, 40);
            pod.Stock.Add(ItemIds.Ration, 24);
            pod.Stock.Add(ItemIds.Biomass, 30);
            pod.Stock.Add("rocket_fuel", 10);
            world.Buildings.Place(BuildingDefs.LandingBeaconId, cx + 8, cy - 4, 0, out _);

            // Palewatch outpost: frozen region with a bauxite line abstracted as rates.
            var body = new BodyDef();
            universe.Bodies.TryGet("palewatch", out body);
            var outpostCargo = new List<Ingredient>
            {
                new Ingredient { ItemId = ItemIds.Water, Count = 40 },
                new Ingredient { ItemId = ItemIds.Ration, Count = 40 },
                new Ingredient { ItemId = "rocket_fuel", Count = 6 },
                new Ingredient { ItemId = "outpost_kit", Count = 1 }
            };
            var palewatch = World.CreateLandingRegion(
                Fnv1a64.HashString(DefaultSeed + ":palewatch:e5"), 96, body, 2, outpostCargo);
            var slot = FreezeForTest(universe, palewatch);
            slot.HasBeacon = true;
            // Their mining line nets +2 bauxite/h (full sim owns it when active).
            slot.RatesPerHour["bauxite"] = 2f;
            universe.FrozenRegions.Add(PalewatchRegionId, slot);

            // Two-way line (M5-T9): steel out, bauxite home.
            universe.AddRoute(1, PalewatchRegionId, "palewatch",
                new List<Ingredient> { new Ingredient { ItemId = "steel_plate", Count = 12 } });
            universe.AddRoute(PalewatchRegionId, 1, "dustloam",
                new List<Ingredient> { new Ingredient { ItemId = "bauxite", Count = 16 } });

            universe.Step();
            return universe;
        }

        private static RegionSlot FreezeForTest(Universe universe, World world)
        {
            // Mirror Universe.Freeze via save/capture (Freeze itself is private).
            var slot = new RegionSlot
            {
                Id = PalewatchRegionId,
                BodyId = "palewatch",
                Seed = world.Seed,
                FrozenSave = SaveSerializer.ToGzipJson(SaveSerializer.Capture(world)),
                ColonistCount = world.Colonists.AliveCount
            };
            foreach (var pile in world.Piles.All.Values)
            {
                slot.Stockpile.TryGetValue(pile.ItemId, out int v);
                slot.Stockpile[pile.ItemId] = v + pile.Count;
            }
            foreach (var building in world.Buildings.All.Values)
            {
                foreach (var entry in building.Stock.SortedEntries())
                {
                    slot.Stockpile.TryGetValue(entry.Key, out int v);
                    slot.Stockpile[entry.Key] = v + entry.Value;
                }
            }
            foreach (var pair in slot.Stockpile)
            {
                slot.Baseline[pair.Key] = pair.Value;
            }
            // Crew upkeep while abstract.
            slot.RatesPerHour[ItemIds.Water] = -2f / GameConstants.HoursPerDay;
            slot.RatesPerHour[ItemIds.Ration] = -2f / GameConstants.HoursPerDay;
            return slot;
        }
    }
}
