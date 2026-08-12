using System;
using System.Collections.Generic;
using System.IO;

namespace Starsoil.RecipeGen
{
    /// <summary>
    /// Recipe generation per docs/plan/04: matrix expansion (material × form via the
    /// hand-maintained validity mask) plus direct translation of chem_graph and
    /// components rows. With M0's empty tables every stage yields zero entries but the
    /// code paths are the real ones the M3 content fills.
    /// </summary>
    public static class Generator
    {
        private const string MaterialSlot = "{material}";
        private const string FormSlot = "{form}";
        private const string VerbSlot = "{verb}";

        public static void Run(Database db, string dataDir)
        {
            ExpandFormMatrix(db, Path.Combine(dataDir, "form_mask.csv"));
            ExpandAlloys(db, Path.Combine(dataDir, "alloys.csv"));
            TranslateGraph(db, Path.Combine(dataDir, "chem_graph.csv"), "C");
            TranslateComponents(db, Path.Combine(dataDir, "components.csv"));
        }

        private static void ExpandFormMatrix(Database db, string path)
        {
            var table = CsvTable.Load(path);
            if (table.Header.Length == 0)
            {
                return;
            }
            for (int r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];
                string materialId = row[0].Trim();
                if (materialId.Length == 0 || !db.Items.TryGetValue(materialId, out var material))
                {
                    continue;
                }
                for (int c = 1; c < table.Header.Length && c < row.Length; c++)
                {
                    string cell = row[c].Trim();
                    if (cell.Length == 0)
                    {
                        continue;
                    }
                    string formId = table.Header[c].Trim();
                    if (!db.Forms.TryGetValue(formId, out var form) || !db.Verbs.TryGetValue(form.Verb, out var verb))
                    {
                        continue;
                    }
                    int tier = Loader.ParseInt(cell, material.Tier);
                    EmitFormedItemRecipe(db, material, form, verb, tier);
                }
            }
        }

        private static void EmitFormedItemRecipe(Database db, ItemDef material, FormDef form, VerbDef verb, int tier)
        {
            string itemId = material.Id + "_" + form.Id;
            if (!db.Items.ContainsKey(itemId))
            {
                db.Items[itemId] = new ItemDef
                {
                    Id = itemId,
                    Zh = material.Zh + form.Zh,
                    En = material.En + " " + form.En,
                    Category = "intermediate",
                    Tier = tier,
                    Tags = new List<string>(material.Tags),
                    IconSpec = material.Id + ":" + form.Id + ":" + verb.Id
                };
            }

            var recipe = new RecipeDef
            {
                Id = "form_" + itemId,
                Verb = verb.Id,
                Tier = tier,
                Family = "B",
                Seconds = verb.BaseSeconds,
                Kw = verb.BaseKw,
                RationaleZh = FillTemplate(db.TemplatesZh, verb.TemplateId, material.Zh, form.Zh, verb.Zh),
                RationaleEn = FillTemplate(db.TemplatesEn, verb.TemplateId, material.En, form.En, verb.En)
            };
            recipe.Inputs.Add(new Ingredient { ItemId = SourceItemFor(db, material), Count = 1 });
            recipe.Outputs.Add(new Ingredient { ItemId = itemId, Count = 1 });
            db.Recipes.Add(recipe);
        }

        /// <summary>Forming consumes the smelted ingot when one exists, otherwise the raw material.</summary>
        private static string SourceItemFor(Database db, ItemDef material)
        {
            string ingotId = material.Id + "_ingot";
            return db.Items.ContainsKey(ingotId) ? ingotId : material.Id;
        }

        private static string FillTemplate(Dictionary<string, string> templates, string templateId,
            string material, string form, string verb)
        {
            if (templateId == null || !templates.TryGetValue(templateId, out string template))
            {
                return string.Empty;
            }
            return template
                .Replace(MaterialSlot, material)
                .Replace(FormSlot, form)
                .Replace(VerbSlot, verb);
        }

        private static void ExpandAlloys(Database db, string path)
        {
            var table = CsvTable.Load(path);
            foreach (var row in table.Rows)
            {
                string id = table.Get(row, "id");
                if (id.Length == 0)
                {
                    continue;
                }
                int tier = Loader.ParseInt(table.Get(row, "tier"), 1);
                db.Items[id] = new ItemDef
                {
                    Id = id,
                    Zh = table.Get(row, "zh"),
                    En = table.Get(row, "en"),
                    Category = "intermediate",
                    Tier = tier,
                    Tags = Loader.SplitMulti(table.Get(row, "tags"))
                };
                db.Verbs.TryGetValue("alloy", out var alloyVerb);
                var recipe = new RecipeDef
                {
                    Id = "alloy_" + id,
                    Verb = "alloy",
                    Tier = tier,
                    Family = "A",
                    Inputs = Loader.ParseIngredients(table.Get(row, "components")),
                    Seconds = alloyVerb != null ? alloyVerb.BaseSeconds : 1,
                    Kw = alloyVerb != null ? alloyVerb.BaseKw : 0,
                    RationaleZh = alloyVerb != null
                        ? FillTemplate(db.TemplatesZh, alloyVerb.TemplateId, table.Get(row, "zh"), string.Empty, alloyVerb.Zh)
                        : string.Empty,
                    RationaleEn = alloyVerb != null
                        ? FillTemplate(db.TemplatesEn, alloyVerb.TemplateId, table.Get(row, "en"), string.Empty, alloyVerb.En)
                        : string.Empty
                };
                recipe.Outputs.Add(new Ingredient { ItemId = id, Count = 1 });
                foreach (var input in recipe.Inputs)
                {
                    db.GetOrStubItem(input.ItemId);
                }
                db.Recipes.Add(recipe);
            }
        }

        private static void TranslateGraph(Database db, string path, string family)
        {
            var table = CsvTable.Load(path);
            foreach (var row in table.Rows)
            {
                string id = table.Get(row, "id");
                if (id.Length == 0)
                {
                    continue;
                }
                var recipe = new RecipeDef
                {
                    Id = id,
                    Verb = table.Get(row, "verb"),
                    Tier = Loader.ParseInt(table.Get(row, "tier"), 2),
                    Family = family,
                    Inputs = Loader.ParseIngredients(table.Get(row, "inputs")),
                    Outputs = Loader.ParseIngredients(table.Get(row, "outputs"))
                };
                foreach (var ing in recipe.Inputs)
                {
                    db.GetOrStubItem(ing.ItemId);
                }
                foreach (var ing in recipe.Outputs)
                {
                    db.GetOrStubItem(ing.ItemId);
                }
                db.Recipes.Add(recipe);
            }
        }

        private static void TranslateComponents(Database db, string path)
        {
            var table = CsvTable.Load(path);
            foreach (var row in table.Rows)
            {
                string id = table.Get(row, "id");
                if (id.Length == 0)
                {
                    continue;
                }
                string category = table.Get(row, "category");
                db.Items[id] = new ItemDef
                {
                    Id = id,
                    Zh = table.Get(row, "zh"),
                    En = table.Get(row, "en"),
                    Category = category.Length > 0 ? category : "component",
                    Tier = Loader.ParseInt(table.Get(row, "tier"), 1)
                };
                var recipe = new RecipeDef
                {
                    Id = "make_" + id,
                    Verb = table.Get(row, "verb"),
                    Tier = Loader.ParseInt(table.Get(row, "tier"), 1),
                    Family = FamilyForCategory(category),
                    Inputs = Loader.ParseIngredients(table.Get(row, "inputs")),
                    Outputs = Loader.ParseIngredients(table.Get(row, "outputs"))
                };
                if (recipe.Outputs.Count == 0)
                {
                    recipe.Outputs.Add(new Ingredient { ItemId = id, Count = 1 });
                }
                foreach (var ing in recipe.Inputs)
                {
                    db.GetOrStubItem(ing.ItemId);
                }
                db.Recipes.Add(recipe);
            }
        }

        private static string FamilyForCategory(string category)
        {
            switch (category)
            {
                case "building": return "F";
                case "vehicle": return "G";
                case "consumable":
                case "food": return "E";
                case "faction": return "H";
                default: return "D";
            }
        }
    }
}
