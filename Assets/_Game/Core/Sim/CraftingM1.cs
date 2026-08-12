using System;
using System.Collections.Generic;
using System.Globalization;

namespace Starsoil.Core
{
    /// <summary>M1 hand recipe (staged interface: replaced by the RecipeGen pipeline at M3-T10).</summary>
    public sealed class RecipeM1
    {
        public string Id;
        /// <summary>Station building kind that runs this recipe (Workbench or Campfire at T0).</summary>
        public BuildingKind Station;
        public List<Ingredient> Inputs = new List<Ingredient>();
        public List<Ingredient> Outputs = new List<Ingredient>();
        /// <summary>Base machine ticks; hand stations apply Balance.HandcraftTimeFactor.</summary>
        public int WorkTicks;
        public string RationaleZh;
        public string RationaleEn;
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

        /// <summary>Effective work ticks at a hand station, including durability slowdown.</summary>
        public static float EffectiveWorkTicks(RecipeM1 recipe, BuildingState station)
        {
            float ticks = recipe.WorkTicks * Balance.HandcraftTimeFactor;
            if (station.Durability < Balance.LowDurabilityThreshold)
            {
                ticks /= Balance.LowDurabilitySpeedFactor;
            }
            return ticks;
        }

        /// <summary>
        /// Parses data/temp_recipes_m1.csv (staged interface, deleted at M3-T10).
        /// Columns: id,station,inputs,outputs,work_ticks,zh_rationale,en_rationale.
        /// </summary>
        public static List<RecipeM1> ParseTempRecipesCsv(IEnumerable<string> lines)
        {
            var result = new List<RecipeM1>();
            bool headerSeen = false;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!headerSeen)
                {
                    headerSeen = true;
                    continue;
                }
                string[] cells = SplitCsvLine(line);
                if (cells.Length < 7)
                {
                    continue;
                }
                var recipe = new RecipeM1
                {
                    Id = cells[0].Trim(),
                    Station = ParseStation(cells[1].Trim()),
                    Inputs = ParseIngredients(cells[2]),
                    Outputs = ParseIngredients(cells[3]),
                    WorkTicks = int.Parse(cells[4].Trim(), CultureInfo.InvariantCulture),
                    RationaleZh = cells[5].Trim(),
                    RationaleEn = cells[6].Trim()
                };
                result.Add(recipe);
            }
            return result;
        }

        private static BuildingKind ParseStation(string value)
        {
            switch (value)
            {
                case "campfire": return BuildingKind.Campfire;
                case "workbench": return BuildingKind.Workbench;
                case "purifier": return BuildingKind.WaterPurifier;
                case "furnace": return BuildingKind.Furnace;
                case "crusher": return BuildingKind.Crusher;
                case "roll_mill": return BuildingKind.RollMill;
                case "press": return BuildingKind.Press;
                case "assembler": return BuildingKind.Assembler;
                case "greenhouse": return BuildingKind.Greenhouse;
                default: return BuildingKind.Workbench;
            }
        }

        private static List<Ingredient> ParseIngredients(string cell)
        {
            var list = new List<Ingredient>();
            foreach (string entry in cell.Split(';'))
            {
                string trimmed = entry.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                string[] parts = trimmed.Split(':');
                int count = parts.Length > 1 ? int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture) : 1;
                list.Add(new Ingredient { ItemId = parts[0].Trim(), Count = count });
            }
            return list;
        }

        private static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        inQuotes = false;
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields.ToArray();
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
