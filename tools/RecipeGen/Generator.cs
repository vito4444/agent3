using System;
using System.Collections.Generic;
using System.IO;

namespace Starsoil.RecipeGen
{
    /// <summary>
    /// Recipe generation per docs/plan/04:
    /// A — ore→ingot smelting (smeltables.csv) and alloying (alloys.csv);
    /// B — ingot→form matrix expansion gated by the hand-maintained validity mask;
    /// C — chem_graph reaction edges;
    /// D..H — components.csv rows (parts, consumables, buildings, vehicles, faction).
    /// Every recipe carries machine/hand stations, tick times and a bilingual rationale
    /// composed from verb templates plus material tag phrases.
    /// </summary>
    public static class Generator
    {
        private const int TicksPerSecond = 10;

        /// <summary>Units produced per ingot for each form (docs/plan/04 sample: 1 ingot → 2 plates).</summary>
        private static readonly Dictionary<string, int> FormYield = new Dictionary<string, int>
        {
            { "plate", 2 }, { "rod", 2 }, { "wire", 3 }, { "gear", 1 },
            { "pipe", 1 }, { "mesh", 2 }, { "powder", 2 }, { "brick", 2 }
        };

        public static void Run(Database db, string dataDir)
        {
            EmitSmelting(db);
            EmitAlloys(db, Path.Combine(dataDir, "alloys.csv"));
            EmitFormMatrix(db, Path.Combine(dataDir, "form_mask.csv"));
            EmitChemGraph(db, Path.Combine(dataDir, "chem_graph.csv"));
            EmitComponents(db, Path.Combine(dataDir, "components.csv"));
            AssignTechNodes(db);
        }

        // ---------------------------------------------------------------- family A

        private static void EmitSmelting(Database db)
        {
            foreach (var smeltable in db.Smeltables.Values)
            {
                string ingotId = smeltable.Id + "_ingot";
                db.Items[ingotId] = new ItemDef
                {
                    Id = ingotId,
                    Zh = smeltable.Zh + "锭",
                    En = smeltable.En + " ingot",
                    Category = "intermediate",
                    Tier = smeltable.Tier,
                    Tags = new List<string>(smeltable.Tags),
                    Mass = 3,
                    IconSpec = smeltable.Id + ":ingot:smelt"
                };
                var verb = db.Verbs["smelt"];
                var recipe = NewRecipe(db, "smelt_" + smeltable.Id, verb, smeltable.Tier, "A");
                recipe.Inputs.Add(new Ingredient { ItemId = smeltable.SourceOre, Count = smeltable.OrePerIngot });
                recipe.Outputs.Add(new Ingredient { ItemId = ingotId, Count = 1 });
                FillRationale(db, recipe, verb, smeltable.Zh, smeltable.En, string.Empty, string.Empty, smeltable.Tags);
                db.Recipes.Add(recipe);
            }
        }

        private static void EmitAlloys(Database db, string path)
        {
            var table = CsvTable.Load(path);
            var verb = db.Verbs["alloy"];
            foreach (var row in table.Rows)
            {
                string id = table.Get(row, "id");
                if (id.Length == 0)
                {
                    continue;
                }
                int tier = Loader.ParseInt(table.Get(row, "tier"), 1);
                var tags = Loader.SplitMulti(table.Get(row, "tags"));
                string ingotId = id + "_ingot";
                db.Items[ingotId] = new ItemDef
                {
                    Id = ingotId,
                    Zh = table.Get(row, "zh") + "锭",
                    En = table.Get(row, "en") + " ingot",
                    Category = "intermediate",
                    Tier = tier,
                    Tags = tags,
                    Mass = 3,
                    IconSpec = id + ":ingot:alloy"
                };
                var recipe = NewRecipe(db, "alloy_" + id, verb, tier, "A");
                recipe.Inputs = Loader.ParseIngredients(table.Get(row, "components"));
                recipe.Outputs.Add(new Ingredient { ItemId = ingotId, Count = 1 });
                foreach (var input in recipe.Inputs)
                {
                    db.GetOrStubItem(input.ItemId);
                }
                FillRationale(db, recipe, verb, table.Get(row, "zh"), table.Get(row, "en"), string.Empty, string.Empty, tags);
                db.Recipes.Add(recipe);
            }
        }

        // ---------------------------------------------------------------- family B

        private static void EmitFormMatrix(Database db, string path)
        {
            var table = CsvTable.Load(path);
            if (table.Header.Length == 0)
            {
                return;
            }
            foreach (var row in table.Rows)
            {
                string materialId = row[0].Trim();
                if (materialId.Length == 0)
                {
                    continue;
                }
                GetMaterialNames(db, materialId, out string zh, out string en, out var tags, out int materialTier);
                if (zh == null)
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
                    int tier = Math.Max(materialTier, Loader.ParseInt(cell, materialTier));
                    string itemId = materialId + "_" + formId;
                    db.Items[itemId] = new ItemDef
                    {
                        Id = itemId,
                        Zh = zh + form.Zh,
                        En = en + " " + form.En,
                        Category = "intermediate",
                        Tier = tier,
                        Tags = new List<string>(tags),
                        Mass = 2,
                        IconSpec = materialId + ":" + formId + ":" + verb.Id
                    };
                    var recipe = NewRecipe(db, "form_" + itemId, verb, tier, "B");
                    recipe.Inputs.Add(new Ingredient { ItemId = materialId + "_ingot", Count = 1 });
                    FormYield.TryGetValue(formId, out int yield);
                    recipe.Outputs.Add(new Ingredient { ItemId = itemId, Count = Math.Max(1, yield) });
                    FillRationale(db, recipe, verb, zh, en, form.Zh, form.En, tags);
                    db.Recipes.Add(recipe);
                }
            }
        }

        private static void GetMaterialNames(Database db, string materialId,
            out string zh, out string en, out List<string> tags, out int tier)
        {
            if (db.Smeltables.TryGetValue(materialId, out var smeltable))
            {
                zh = smeltable.Zh;
                en = smeltable.En;
                tags = smeltable.Tags;
                tier = smeltable.Tier;
                return;
            }
            string ingotId = materialId + "_ingot";
            if (db.Items.TryGetValue(ingotId, out var alloyIngot))
            {
                zh = alloyIngot.Zh.EndsWith("锭") ? alloyIngot.Zh.Substring(0, alloyIngot.Zh.Length - 1) : alloyIngot.Zh;
                en = alloyIngot.En.EndsWith(" ingot") ? alloyIngot.En.Substring(0, alloyIngot.En.Length - 6) : alloyIngot.En;
                tags = alloyIngot.Tags;
                tier = alloyIngot.Tier;
                return;
            }
            zh = null;
            en = null;
            tags = null;
            tier = 0;
        }

        // ---------------------------------------------------------------- family C

        private static void EmitChemGraph(Database db, string path)
        {
            var table = CsvTable.Load(path);
            foreach (var row in table.Rows)
            {
                string id = table.Get(row, "id");
                if (id.Length == 0)
                {
                    continue;
                }
                string verbId = table.Get(row, "verb");
                db.Verbs.TryGetValue(verbId, out var verb);
                var recipe = NewRecipe(db, id, verb, Loader.ParseInt(table.Get(row, "tier"), 2), "C");
                recipe.Inputs = Loader.ParseIngredients(table.Get(row, "inputs"));
                recipe.Outputs = Loader.ParseIngredients(table.Get(row, "outputs"));
                foreach (var ing in recipe.Inputs)
                {
                    db.GetOrStubItem(ing.ItemId);
                }
                foreach (var ing in recipe.Outputs)
                {
                    db.GetOrStubItem(ing.ItemId);
                }
                string primaryZh = recipe.Outputs.Count > 0 && db.Items.TryGetValue(recipe.Outputs[0].ItemId, out var outItem)
                    ? outItem.Zh : id;
                string primaryEn = recipe.Outputs.Count > 0 && db.Items.TryGetValue(recipe.Outputs[0].ItemId, out var outItem2)
                    ? outItem2.En : id;
                var outTags = recipe.Outputs.Count > 0 && db.Items.TryGetValue(recipe.Outputs[0].ItemId, out var outItem3)
                    ? outItem3.Tags : new List<string>();
                FillRationale(db, recipe, verb, primaryZh, primaryEn, string.Empty, string.Empty, outTags);
                db.Recipes.Add(recipe);
            }
        }

        // ---------------------------------------------------------------- families D..H

        private static void EmitComponents(Database db, string path)
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
                var outputs = Loader.ParseIngredients(table.Get(row, "outputs"));
                // The produced item id: explicit outputs win; otherwise the row id.
                string producedId = outputs.Count > 0 ? outputs[0].ItemId : id;
                int tier = Loader.ParseInt(table.Get(row, "tier"), 1);
                var item = new ItemDef
                {
                    Id = producedId,
                    Zh = table.Get(row, "zh"),
                    En = table.Get(row, "en"),
                    Category = category.Length > 0 ? category : "component",
                    Tier = tier,
                    Mass = 2
                };
                // Do not overwrite richer definitions (e.g. items_extra water).
                if (!db.Items.TryGetValue(producedId, out var existing) || existing.IsStub)
                {
                    db.Items[producedId] = item;
                }

                string verbId = table.Get(row, "verb");
                db.Verbs.TryGetValue(verbId, out var verb);
                var recipe = NewRecipe(db, "make_" + id, verb, tier, FamilyForCategory(category));
                recipe.Inputs = Loader.ParseIngredients(table.Get(row, "inputs"));
                recipe.Outputs = outputs.Count > 0 ? outputs : new List<Ingredient> { new Ingredient { ItemId = producedId, Count = 1 } };
                recipe.FactionLocked = category == "faction";
                foreach (var ing in recipe.Inputs)
                {
                    db.GetOrStubItem(ing.ItemId);
                }
                FillRationale(db, recipe, verb, item.Zh, item.En, string.Empty, string.Empty, item.Tags);
                db.Recipes.Add(recipe);
            }
        }

        private static void AssignTechNodes(Database db)
        {
            foreach (var recipe in db.Recipes)
            {
                if (db.RecipeToTechNode.TryGetValue(recipe.Id, out string node))
                {
                    recipe.TechNode = node;
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        private static RecipeDef NewRecipe(Database db, string id, VerbDef verb, int tier, string family)
        {
            return new RecipeDef
            {
                Id = id,
                Verb = verb?.Id ?? string.Empty,
                Tier = tier,
                Family = family,
                Seconds = verb?.BaseSeconds ?? 1,
                Kw = verb?.BaseKw ?? 0,
                WorkTicks = (int)((verb?.BaseSeconds ?? 1) * TicksPerSecond),
                MachineStation = verb?.Machine ?? string.Empty,
                HandStation = verb?.HandVersion ?? string.Empty
            };
        }

        private static void FillRationale(Database db, RecipeDef recipe, VerbDef verb,
            string materialZh, string materialEn, string formZh, string formEn, List<string> tags)
        {
            if (verb == null)
            {
                return;
            }
            bool hasForm = !string.IsNullOrEmpty(formZh);
            var zhTable = hasForm ? db.TemplatesZh : db.TemplatesZhNf;
            var enTable = hasForm ? db.TemplatesEn : db.TemplatesEnNf;
            if (!zhTable.TryGetValue(verb.TemplateId, out string zhTemplate) || zhTemplate.Length == 0)
            {
                db.TemplatesZh.TryGetValue(verb.TemplateId, out zhTemplate);
            }
            if (!enTable.TryGetValue(verb.TemplateId, out string enTemplate) || (enTemplate?.Length ?? 0) == 0)
            {
                db.TemplatesEn.TryGetValue(verb.TemplateId, out enTemplate);
            }
            if (zhTemplate == null)
            {
                return;
            }
            TagPhrase phrase = null;
            if (tags != null)
            {
                foreach (string tag in tags)
                {
                    if (db.TagPhrases.TryGetValue(tag, out phrase))
                    {
                        break;
                    }
                }
            }
            recipe.RationaleZh = zhTemplate
                .Replace("{material}", materialZh)
                .Replace("{form}", formZh)
                .Replace("{attr}", phrase?.ZhAttr ?? "工艺成熟")
                .Replace("{use}", phrase?.ZhUse ?? "用于基地建设与生产");
            recipe.RationaleEn = (enTemplate ?? string.Empty)
                .Replace("{material}", materialEn)
                .Replace("{form}", formEn)
                .Replace("{attr}", phrase?.EnAttr ?? "the process is proven")
                .Replace("{use}", phrase?.EnUse ?? "used across base building and production");
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
                case "intermediate": return "D";
                default: return "D";
            }
        }
    }
}
