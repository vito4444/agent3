using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    public sealed class AchievementUnlockedEvent : ISimEvent
    {
        public string Id;
    }

    /// <summary>
    /// Achievement tracker (docs/plan/10 locked list of 20). The Core layer decides when
    /// an achievement unlocks (deterministic, testable); the Steam bridge (Game.UI,
    /// STEAM define) mirrors unlocks to Steamworks and is a no-op elsewhere.
    /// Predicates are polled hourly on universe state plus counters bumped by events.
    /// </summary>
    public sealed class AchievementSystem
    {
        public static readonly string[] All =
        {
            "first_night", "powered_up", "hands_obsolete", "steel_apprentice", "first_electrolysis",
            "recipe_hunter", "industrial_codex", "liftoff", "two_worlds", "spaceport_era",
            "first_contact", "mutual_gain", "most_favored", "not_one_step", "tear_the_ultimatum",
            "behind_high_walls", "double_harvest", "lord_protector", "hegemon", "warp_ignition"
        };

        public HashSet<string> Unlocked { get; } = new HashSet<string>();
        public int TradesCompleted;
        public int RaidsRepelled;
        public int RouteSteadyDays;
        public long FirstRouteTransitHour = -1;

        private const int RecipeHunterCount = 100;
        private const int IndustrialCodexCount = 250;
        private const int MutualGainTrades = 50;
        private const int MostFavoredAttitude = 60;
        private const int NotOneStepRaids = 10;
        private const int SpaceportRouteDays = 5;

        /// <summary>Machine kinds whose presence covers the 12 hand→machine mapping rows.</summary>
        private static readonly BuildingKind[] MappingMachines =
        {
            BuildingKind.Furnace, BuildingKind.Crusher, BuildingKind.Press, BuildingKind.Assembler,
            BuildingKind.Distiller, BuildingKind.CultureVat, BuildingKind.Miner, BuildingKind.IceMiner,
            BuildingKind.SolarPanel, BuildingKind.BotStation
        };

        public void ObserveEvents(Universe universe)
        {
            // Unlock() appends to the same event list; iterate the pre-existing slice only.
            int existing = universe.ActiveWorld.Events.Count;
            for (int i = 0; i < existing; i++)
            {
                var evt = universe.ActiveWorld.Events[i];
                switch (evt)
                {
                    case DealSettledEvent _:
                        TradesCompleted++;
                        break;
                    case RaidResolvedEvent raid when raid.Repelled:
                        RaidsRepelled++;
                        break;
                    case RocketLaunchedEvent _:
                        Unlock(universe, "liftoff");
                        break;
                    case OccupationSettledEvent occupation:
                        if (occupation.BodyId == "sleetfall")
                        {
                            Unlock(universe, "double_harvest");
                        }
                        Unlock(universe, "tear_the_ultimatum");
                        break;
                    case VassalTreatyEvent treaty when treaty.Signed:
                        Unlock(universe, "lord_protector");
                        break;
                    case VictoryEvent victory:
                        Unlock(universe, victory.Path == "hegemony" ? "hegemon" : "warp_ignition");
                        break;
                    case TransitArrivedEvent _:
                        if (FirstRouteTransitHour < 0 && universe.Routes.Count > 0)
                        {
                            FirstRouteTransitHour = universe.Tick / GameConstants.TicksPerHour;
                        }
                        break;
                }
            }
        }

        public void HourlyCheck(Universe universe)
        {
            var world = universe.ActiveWorld;
            if (world.Day >= 1)
            {
                Unlock(universe, "first_night");
            }
            if (world.Networks.LastSupplyKw > 0f)
            {
                Unlock(universe, "powered_up");
            }
            if (world.Stats.CraftedOf("steel_ingot") > 0)
            {
                Unlock(universe, "steel_apprentice");
            }
            if (HasAllMappingMachines(world))
            {
                Unlock(universe, "hands_obsolete");
            }
            int unlockedRecipes = CountUnlockedRecipes(world);
            if (unlockedRecipes >= RecipeHunterCount)
            {
                Unlock(universe, "recipe_hunter");
            }
            if (unlockedRecipes >= IndustrialCodexCount)
            {
                Unlock(universe, "industrial_codex");
            }
            if (universe.FrozenRegions.Count > 0)
            {
                Unlock(universe, "two_worlds");
            }
            if (universe.FactionLayerVisible)
            {
                Unlock(universe, "first_contact");
            }
            if (TradesCompleted >= MutualGainTrades)
            {
                Unlock(universe, "mutual_gain");
            }
            var merchant = universe.FactionsSandbox.Get(FactionSystem.MerchantId);
            if (merchant != null && merchant.AttitudeToPlayer >= MostFavoredAttitude)
            {
                Unlock(universe, "most_favored");
            }
            if (RaidsRepelled >= NotOneStepRaids)
            {
                Unlock(universe, "not_one_step");
            }
            if (universe.ActiveWorld.Tech.IsUnlocked("branch_superconductor_grid") ||
                universe.ActiveWorld.Tech.IsUnlocked("branch_phase_armor"))
            {
                Unlock(universe, "behind_high_walls");
            }
            if (FirstRouteTransitHour >= 0 && universe.Routes.Count > 0 &&
                universe.Tick / GameConstants.TicksPerHour - FirstRouteTransitHour >=
                SpaceportRouteDays * GameConstants.HoursPerDay)
            {
                Unlock(universe, "spaceport_era");
            }
            // Electrolysis: any water-splitting output present counts once produced.
            if (world.Stats.CraftedOf("hydrogen") > 0 || world.Stats.CraftedOf("oxygen_gas") > 0)
            {
                Unlock(universe, "first_electrolysis");
            }
        }

        private static bool HasAllMappingMachines(World world)
        {
            var present = new HashSet<BuildingKind>();
            foreach (var pair in world.Buildings.All)
            {
                if (BuildingDefs.TryGet(pair.Value.DefId, out var def))
                {
                    present.Add(def.Kind);
                }
            }
            foreach (var kind in MappingMachines)
            {
                if (!present.Contains(kind))
                {
                    return false;
                }
            }
            return true;
        }

        private static int CountUnlockedRecipes(World world)
        {
            int count = 0;
            foreach (var pair in world.Crafting.Recipes)
            {
                if (world.Tech.IsRecipeUnlocked(pair.Key))
                {
                    count++;
                }
            }
            return count;
        }

        private void Unlock(Universe universe, string id)
        {
            if (Unlocked.Add(id))
            {
                universe.ActiveWorld.Events.Add(new AchievementUnlockedEvent { Id = id });
            }
        }
    }
}
