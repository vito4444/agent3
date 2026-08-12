using System;
using System.Collections.Generic;
using System.Globalization;

namespace Starsoil.Core
{
    /// <summary>
    /// Runtime recipe (from the RecipeGen catalog since M3-T10). One recipe can run on
    /// its machine station at full speed and, when a hand station exists for the verb,
    /// on that hand station at Balance.HandcraftTimeFactor slower.
    /// </summary>
    public sealed class RecipeM1
    {
        public string Id;
        /// <summary>Machine station kind that runs this recipe at full speed.</summary>
        public BuildingKind Station;
        /// <summary>Hand station kind (Generic = none).</summary>
        public BuildingKind HandStation = BuildingKind.Generic;
        public List<Ingredient> Inputs = new List<Ingredient>();
        public List<Ingredient> Outputs = new List<Ingredient>();
        /// <summary>Machine ticks; hand stations apply Balance.HandcraftTimeFactor.</summary>
        public int WorkTicks;
        public string RationaleZh;
        public string RationaleEn;
        public bool FactionLocked;

        public bool HasHandStation => HandStation != BuildingKind.Generic;

        /// <summary>Can this recipe run on the given station kind?</summary>
        public bool RunsOn(BuildingKind kind, out bool handSpeed)
        {
            if (kind == Station)
            {
                handSpeed = false;
                return true;
            }
            if (HasHandStation && kind == HandStation)
            {
                handSpeed = true;
                return true;
            }
            handSpeed = false;
            return false;
        }
    }

    public sealed class CraftOrder
    {
        public int Id;
        public string RecipeId;
        /// <summary>Remaining crafts for count orders; -1 for maintain orders.</summary>
        public int Remaining;
        /// <summary>For maintain orders: craft while global stock of the first output is below this.</summary>
        public int MaintainTarget;

        public bool IsMaintain => Remaining < 0;
    }

    /// <summary>
    /// Crafting at hand stations (docs/plan/03 machine model, hand special case):
    /// orders per station, inputs hauled into the station buffer, work executed by a
    /// colonist standing at the station, outputs accumulate in the buffer.
    /// </summary>
    public sealed class CraftingSystem
    {
        private readonly Dictionary<string, RecipeM1> _recipes = new Dictionary<string, RecipeM1>();
        /// <summary>stationBuildingId → orders (player-ordered list).</summary>
        public Dictionary<int, List<CraftOrder>> OrdersByStation { get; } = new Dictionary<int, List<CraftOrder>>();
        private int _nextOrderId = 1;

        public IReadOnlyDictionary<string, RecipeM1> Recipes => _recipes;

        public void LoadRecipes(IEnumerable<RecipeM1> recipes)
        {
            _recipes.Clear();
            foreach (var recipe in recipes)
            {
                _recipes[recipe.Id] = recipe;
            }
        }

        public bool TryGetRecipe(string id, out RecipeM1 recipe) => _recipes.TryGetValue(id, out recipe);

        public CraftOrder AddOrder(int stationId, string recipeId, int count, int maintainTarget)
        {
            if (!OrdersByStation.TryGetValue(stationId, out var list))
            {
                list = new List<CraftOrder>();
                OrdersByStation.Add(stationId, list);
            }
            var order = new CraftOrder
            {
                Id = _nextOrderId,
                RecipeId = recipeId,
                Remaining = count,
                MaintainTarget = maintainTarget
            };
            _nextOrderId++;
            list.Add(order);
            return order;
        }

        public bool RemoveOrder(int stationId, int orderId)
        {
            if (!OrdersByStation.TryGetValue(stationId, out var list))
            {
                return false;
            }
            return list.RemoveAll(o => o.Id == orderId) > 0;
        }

        /// <summary>The order this station should work on now, or null.</summary>
        public CraftOrder ActiveOrder(World world, BuildingState station)
        {
            if (!OrdersByStation.TryGetValue(station.Id, out var list))
            {
                return null;
            }
            foreach (var order in list)
            {
                if (!_recipes.TryGetValue(order.RecipeId, out var recipe))
                {
                    continue;
                }
                if (order.IsMaintain)
                {
                    string outputItem = recipe.Outputs[0].ItemId;
                    if (world.CountItemEverywhere(outputItem) < order.MaintainTarget)
                    {
                        return order;
                    }
                }
                else if (order.Remaining > 0)
                {
                    return order;
                }
            }
            return null;
        }

        /// <summary>Missing input units the station still needs hauled for the active order.</summary>
        public int MissingInput(BuildingState station, RecipeM1 recipe, string itemId)
        {
            foreach (var input in recipe.Inputs)
            {
                if (input.ItemId == itemId)
                {
                    return input.Count - station.Stock.Get(itemId) - station.Inbound.Get(itemId);
                }
            }
            return 0;
        }

        public bool InputsReady(BuildingState station, RecipeM1 recipe)
        {
            foreach (var input in recipe.Inputs)
            {
                if (station.Stock.Get(input.ItemId) < input.Count)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Consumes inputs and emits outputs into the station buffer; updates the order.</summary>
        public void CompleteCraft(World world, BuildingState station, CraftOrder order, RecipeM1 recipe)
        {
            foreach (var input in recipe.Inputs)
            {
                station.Stock.TryRemove(input.ItemId, input.Count);
            }
            foreach (var output in recipe.Outputs)
            {
                station.Stock.Add(output.ItemId, output.Count);
                world.Stats.CountCrafted(output.ItemId, output.Count);
            }
            if (!order.IsMaintain)
            {
                order.Remaining--;
                if (order.Remaining <= 0 && OrdersByStation.TryGetValue(station.Id, out var list))
                {
                    list.RemoveAll(o => o.Id == order.Id);
                }
            }
            world.Events.Add(new CraftCompletedEvent { StationId = station.Id, RecipeId = recipe.Id });
        }

        /// <summary>Effective work ticks on the given station, including hand penalty and
        /// durability slowdown.</summary>
        public static float EffectiveWorkTicks(RecipeM1 recipe, BuildingState station, bool handSpeed)
        {
            float ticks = recipe.WorkTicks * (handSpeed ? Balance.HandcraftTimeFactor : 1f);
            if (station.Durability < Balance.LowDurabilityThreshold)
            {
                ticks /= Balance.LowDurabilitySpeedFactor;
            }
            return ticks;
        }

        internal void RestoreFrom(List<SavedCraftOrder> saved)
        {
            OrdersByStation.Clear();
            _nextOrderId = 1;
            foreach (var s in saved)
            {
                if (!OrdersByStation.TryGetValue(s.StationId, out var list))
                {
                    list = new List<CraftOrder>();
                    OrdersByStation.Add(s.StationId, list);
                }
                list.Add(new CraftOrder
                {
                    Id = s.Id,
                    RecipeId = s.RecipeId,
                    Remaining = s.Remaining,
                    MaintainTarget = s.MaintainTarget
                });
                if (s.Id >= _nextOrderId)
                {
                    _nextOrderId = s.Id + 1;
                }
            }
        }
    }
}
