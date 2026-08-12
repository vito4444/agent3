using Starsoil.Core;

namespace Starsoil.BalanceSim.Scenarios
{
    /// <summary>
    /// E1 survival baseline (docs/plan/09 M1-T12, docs/plan/08 test strategy):
    /// a scripted hands-only opening — designate starter nodes, place a campfire and a
    /// workbench, keep water/ration/fiber/bandage stocks via maintain orders — then let
    /// the colony autonomy run for ten game days on standard difficulty.
    /// </summary>
    public static class E1Scenario
    {
        public const ulong DefaultSeed = 2026UL;
        public const int RegionSize = 192;
        public const int Days = 10;

        public static World Run(ulong seed)
        {
            var world = TestUtil.NewColonyWorld(seed, RegionSize);
            ApplyOpeningScript(world);
            TestUtil.Run(world, Days * GameConstants.TicksPerDay);
            return world;
        }

        /// <summary>Survival-relevant resources; a sensible opening designates these, not everything.</summary>
        private static readonly string[] OpeningResources =
        {
            ItemIds.Ice, ItemIds.Biomass, ItemIds.Carbon, ItemIds.IronOre
        };

        public static void ApplyOpeningScript(World world)
        {
            // Designate the survival-relevant nodes near the crash site.
            foreach (var pair in world.Nodes.All)
            {
                var node = pair.Value;
                bool near = System.Math.Abs(node.X - world.StartX) + System.Math.Abs(node.Y - world.StartY) <=
                            Balance.GuaranteedDepositRadius;
                bool relevant = System.Array.IndexOf(OpeningResources, node.ItemId) >= 0;
                if (near && relevant)
                {
                    world.Commands.Enqueue(new ToggleNodeDesignationCommand { NodeId = node.Id, Designated = true });
                }
            }

            // Stations next to the pod.
            world.Commands.Enqueue(new PlaceBlueprintCommand
            {
                DefId = BuildingDefs.CampfireId,
                X = world.StartX + 4,
                Y = world.StartY,
                Rotation = 0
            });
            world.Commands.Enqueue(new PlaceBlueprintCommand
            {
                DefId = BuildingDefs.WorkbenchId,
                X = world.StartX + 4,
                Y = world.StartY + 3,
                Rotation = 0
            });

            // One tick so the blueprints exist, then queue the maintain orders once the
            // buildings complete (orders reference real station ids).
            world.Step();
        }

        /// <summary>Adds maintain orders as soon as the stations exist. Call every game hour.</summary>
        public static void TickOrders(World world)
        {
            TryEnsureOrder(world, BuildingKind.Campfire, "make_water_melt", 8);
            TryEnsureOrder(world, BuildingKind.Workbench, "make_ration", 8);
            TryEnsureOrder(world, BuildingKind.Workbench, "make_fiber", 4);
            TryEnsureOrder(world, BuildingKind.Workbench, "make_bandage", 2);
        }

        private static void TryEnsureOrder(World world, BuildingKind stationKind, string recipeId, int maintain)
        {
            var station = world.Buildings.FindFirstOfKind(stationKind);
            if (station == null)
            {
                return;
            }
            if (world.Crafting.OrdersByStation.TryGetValue(station.Id, out var orders))
            {
                foreach (var order in orders)
                {
                    if (order.RecipeId == recipeId)
                    {
                        return;
                    }
                }
            }
            world.Crafting.AddOrder(station.Id, recipeId, -1, maintain);
        }

        /// <summary>Full scripted run with hourly order upkeep.</summary>
        public static World RunWithUpkeep(ulong seed)
        {
            var world = TestUtil.NewColonyWorld(seed, RegionSize);
            ApplyOpeningScript(world);
            long targetTicks = (long)Days * GameConstants.TicksPerDay;
            while (world.Tick < targetTicks)
            {
                if (world.Tick % GameConstants.TicksPerHour == 0)
                {
                    TickOrders(world);
                }
                world.Step();
            }
            return world;
        }
    }
}
