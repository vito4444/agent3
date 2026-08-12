using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Starsoil.Core
{
    /// <summary>
    /// Parses GeneratedData/recipes.json (the RecipeGen catalog, docs/plan/04) into
    /// runtime recipes. Station building ids resolve to station kinds via BuildingDefs;
    /// unknown ids leave BuildingKind.Generic (the recipe simply cannot run until the
    /// building exists — the RecipeGen validation keeps this from shipping).
    /// </summary>
    public static class CatalogRecipes
    {
        public static List<RecipeM1> ParseJson(string json)
        {
            var result = new List<RecipeM1>();
            var root = JObject.Parse(json);
            if (!(root["recipes"] is JArray recipes))
            {
                return result;
            }
            foreach (var token in recipes)
            {
                if (!(token is JObject r))
                {
                    continue;
                }
                var recipe = new RecipeM1
                {
                    Id = r.Value<string>("Id") ?? string.Empty,
                    WorkTicks = r.Value<int?>("WorkTicks") ?? 10,
                    Station = ResolveKind(r.Value<string>("MachineStation")),
                    HandStation = ResolveKind(r.Value<string>("HandStation")),
                    RationaleZh = r.Value<string>("RationaleZh") ?? string.Empty,
                    RationaleEn = r.Value<string>("RationaleEn") ?? string.Empty,
                    FactionLocked = r.Value<bool?>("FactionLocked") ?? false
                };
                ReadIngredients(r["Inputs"] as JArray, recipe.Inputs);
                ReadIngredients(r["Outputs"] as JArray, recipe.Outputs);
                if (recipe.Id.Length > 0)
                {
                    result.Add(recipe);
                }
            }
            return result;
        }

        private static void ReadIngredients(JArray array, List<Ingredient> target)
        {
            if (array == null)
            {
                return;
            }
            foreach (var token in array)
            {
                if (token is JObject ing)
                {
                    target.Add(new Ingredient
                    {
                        ItemId = ing.Value<string>("ItemId") ?? string.Empty,
                        Count = ing.Value<int?>("Count") ?? 1
                    });
                }
            }
        }

        private static BuildingKind ResolveKind(string buildingId)
        {
            if (!string.IsNullOrEmpty(buildingId) && BuildingDefs.TryGet(buildingId, out var def))
            {
                return def.Kind;
            }
            return BuildingKind.Generic;
        }
    }
}
