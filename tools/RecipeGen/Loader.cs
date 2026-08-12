using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Starsoil.RecipeGen
{
    /// <summary>
    /// Loads the hand-maintained CSV tables (docs/plan/04 data sources) into a Database.
    /// Missing files load as empty tables so the pipeline degrades to "0 items, 0 recipes"
    /// instead of crashing — M0 ships headers only; M3 fills the content.
    /// </summary>
    public static class Loader
    {
        public static Database Load(string dataDir)
        {
            var db = new Database();
            LoadTemplates(db, Path.Combine(dataDir, "rationale_templates.csv"));
            LoadVerbs(db, Path.Combine(dataDir, "verbs.csv"));
            LoadForms(db, Path.Combine(dataDir, "forms.csv"));
            LoadMaterials(db, Path.Combine(dataDir, "materials.csv"));
            LoadTechNodes(db, Path.Combine(dataDir, "tech_nodes.csv"));
            return db;
        }

        public static List<string> SplitMulti(string cell)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(cell))
            {
                return result;
            }
            foreach (string part in cell.Split(';'))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }
            return result;
        }

        public static List<Ingredient> ParseIngredients(string cell)
        {
            var result = new List<Ingredient>();
            foreach (string entry in SplitMulti(cell))
            {
                string[] parts = entry.Split(':');
                int count = 1;
                if (parts.Length > 1)
                {
                    count = int.Parse(parts[1], CultureInfo.InvariantCulture);
                }
                result.Add(new Ingredient { ItemId = parts[0].Trim(), Count = count });
            }
            return result;
        }

        public static int ParseInt(string cell, int fallback)
        {
            return int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        }

        public static double ParseDouble(string cell, double fallback)
        {
            return double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }

        private static void LoadMaterials(Database db, string path)
        {
            var table = CsvTable.Load(path);
            foreach (var row in table.Rows)
            {
                var item = new ItemDef
                {
                    Id = table.Get(row, "id"),
                    Zh = table.Get(row, "zh"),
                    En = table.Get(row, "en"),
                    Category = "raw",
                    Tier = ParseInt(table.Get(row, "tier"), 0),
                    Tags = SplitMulti(table.Get(row, "tags")),
                    SourceBodies = SplitMulti(table.Get(row, "source_bodies")),
                    Mass = ParseDouble(table.Get(row, "mass"), 0)
                };
                if (item.Id.Length > 0)
                {
                    db.Items[item.Id] = item;
                }
            }
        }

        private static void LoadVerbs(Database db, string path)
        {
            var table = CsvTable.Load(path);
            foreach (var row in table.Rows)
            {
                var verb = new VerbDef
                {
                    Id = table.Get(row, "id"),
                    Zh = table.Get(row, "zh"),
                    En = table.Get(row, "en"),
                    Machine = table.Get(row, "machine"),
                    HandVersion = table.Get(row, "hand_version"),
                    BaseSeconds = ParseDouble(table.Get(row, "base_seconds"), 1),
                    BaseKw = ParseDouble(table.Get(row, "base_kw"), 0),
                    TemplateId = table.Get(row, "template_id")
                };
                if (verb.Id.Length > 0)
                {
                    db.Verbs[verb.Id] = verb;
                }
            }
        }

        private static void LoadForms(Database db, string path)
        {
            var table = CsvTable.Load(path);
            foreach (var row in table.Rows)
            {
                var form = new FormDef
                {
                    Id = table.Get(row, "id"),
                    Zh = table.Get(row, "zh"),
                    En = table.Get(row, "en"),
                    Verb = table.Get(row, "verb")
                };
                if (form.Id.Length > 0)
                {
                    db.Forms[form.Id] = form;
                }
            }
        }

        private static void LoadTechNodes(Database db, string path)
        {
            var table = CsvTable.Load(path);
            foreach (var row in table.Rows)
            {
                var node = new TechNodeDef
                {
                    Id = table.Get(row, "id"),
                    Zh = table.Get(row, "zh"),
                    En = table.Get(row, "en"),
                    Domain = table.Get(row, "domain"),
                    Prereqs = SplitMulti(table.Get(row, "prereqs"))
                };
                if (node.Id.Length > 0)
                {
                    db.TechNodes[node.Id] = node;
                }
            }
        }

        private static void LoadTemplates(Database db, string path)
        {
            var table = CsvTable.Load(path);
            foreach (var row in table.Rows)
            {
                string id = table.Get(row, "template_id");
                if (id.Length == 0)
                {
                    continue;
                }
                db.TemplatesZh[id] = table.Get(row, "zh");
                db.TemplatesEn[id] = table.Get(row, "en");
            }
        }
    }
}
