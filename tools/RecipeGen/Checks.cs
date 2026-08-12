using System;
using System.Collections.Generic;
using System.Linq;

namespace Starsoil.RecipeGen
{
    public sealed class CheckResult
    {
        public string Name;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public string Note = string.Empty;
        public bool Passed => Errors.Count == 0;
    }

    /// <summary>
    /// The 8 validation gates from docs/plan/04. All are real graph checks; on M0's
    /// empty data they pass vacuously, and M3 content must keep them at zero errors.
    /// </summary>
    public static class Checks
    {
        private const int MaxRecipesPerTechNode = 14;
        private const double MassGainWarnFactor = 1.3;
        private const string StartBody = "dustloam";

        /// <summary>Crash-pod starting inventory (docs/plan/02) counts as reachable roots.</summary>
        private static readonly string[] StartInventoryItems =
        {
            "emergency_ration",
            "algae_seed",
            "oxygen_bottle"
        };

        private static readonly HashSet<string> TerminalCategories = new HashSet<string>
        {
            "building", "vehicle", "consumable", "food", "terminal", "faction"
        };

        public static List<CheckResult> RunAll(Database db)
        {
            return new List<CheckResult>
            {
                Reachability(db),
                OrphanItems(db),
                DeadEndRecipes(db),
                EnergyLoops(db),
                MassConservation(db),
                UnlockCoverage(db),
                IconSpecs(db),
                LocalizationKeys(db)
            };
        }

        private static CheckResult Reachability(Database db)
        {
            var result = new CheckResult { Name = "1 reachability (start planet + crash-pod inventory)" };
            var reachable = new HashSet<string>();
            foreach (var item in db.Items.Values)
            {
                if (item.SourceBodies.Contains(StartBody))
                {
                    reachable.Add(item.Id);
                }
            }
            foreach (string id in StartInventoryItems)
            {
                if (db.Items.ContainsKey(id))
                {
                    reachable.Add(id);
                }
            }

            bool progress = true;
            var reachedRecipes = new HashSet<string>();
            while (progress)
            {
                progress = false;
                foreach (var recipe in db.Recipes)
                {
                    if (reachedRecipes.Contains(recipe.Id))
                    {
                        continue;
                    }
                    if (recipe.Inputs.All(i => reachable.Contains(i.ItemId)))
                    {
                        reachedRecipes.Add(recipe.Id);
                        foreach (var output in recipe.Outputs)
                        {
                            if (reachable.Add(output.ItemId))
                            {
                                progress = true;
                            }
                        }
                        progress = true;
                    }
                }
            }

            // T0–T2 recipes and the rocket chain (family G, tier ≤3) must be reachable
            // from the start planet alone; faction-locked recipes are exempt (docs/plan/04).
            foreach (var recipe in db.Recipes)
            {
                if (recipe.FactionLocked || reachedRecipes.Contains(recipe.Id))
                {
                    continue;
                }
                bool mustReach = recipe.Tier <= 2 || (recipe.Family == "G" && recipe.Tier <= 3);
                if (mustReach)
                {
                    result.Errors.Add("unreachable recipe: " + recipe.Id + " (tier " + recipe.Tier + ", family " + recipe.Family + ")");
                }
            }
            result.Note = reachedRecipes.Count + "/" + db.Recipes.Count + " recipes reachable";
            return result;
        }

        private static CheckResult OrphanItems(Database db)
        {
            var result = new CheckResult { Name = "2 no orphan items" };
            var produced = new HashSet<string>(db.Recipes.SelectMany(r => r.Outputs).Select(i => i.ItemId));
            var consumed = new HashSet<string>(db.Recipes.SelectMany(r => r.Inputs).Select(i => i.ItemId));

            foreach (var item in db.Items.Values)
            {
                bool isRaw = item.Category == "raw";
                if (!isRaw && !produced.Contains(item.Id))
                {
                    result.Errors.Add("item never produced: " + item.Id);
                }
                if (!consumed.Contains(item.Id) && !TerminalCategories.Contains(item.Category) && !isRaw)
                {
                    result.Errors.Add("item never consumed and not terminal: " + item.Id);
                }
                else if (isRaw && !consumed.Contains(item.Id) && db.Recipes.Count > 0)
                {
                    result.Warnings.Add("raw material unused: " + item.Id);
                }
            }
            return result;
        }

        private static CheckResult DeadEndRecipes(Database db)
        {
            var result = new CheckResult { Name = "3 no dead-end recipes" };
            var consumed = new HashSet<string>(db.Recipes.SelectMany(r => r.Inputs).Select(i => i.ItemId));
            foreach (var recipe in db.Recipes)
            {
                bool anyUseful = recipe.Outputs.Any(o =>
                    consumed.Contains(o.ItemId) ||
                    (db.Items.TryGetValue(o.ItemId, out var item) && TerminalCategories.Contains(item.Category)));
                if (!anyUseful)
                {
                    result.Errors.Add("dead-end recipe (no output is consumed or terminal): " + recipe.Id);
                }
            }
            return result;
        }

        private static CheckResult EnergyLoops(Database db)
        {
            var result = new CheckResult { Name = "4 energy monotonic (no free-energy loops)" };

            // Build item→item edges through recipes; a cycle whose recipes add zero total
            // energy is a free-energy loop suspect.
            var edges = new Dictionary<string, List<(string to, double energy)>>();
            foreach (var recipe in db.Recipes)
            {
                double energy = recipe.Seconds * recipe.Kw;
                foreach (var input in recipe.Inputs)
                {
                    if (!edges.TryGetValue(input.ItemId, out var list))
                    {
                        list = new List<(string, double)>();
                        edges.Add(input.ItemId, list);
                    }
                    foreach (var output in recipe.Outputs)
                    {
                        list.Add((output.ItemId, energy));
                    }
                }
            }

            var visiting = new HashSet<string>();
            var done = new HashSet<string>();
            var stack = new List<string>();

            foreach (string start in edges.Keys.ToList())
            {
                DetectZeroEnergyCycle(start, edges, visiting, done, stack, result);
            }
            return result;
        }

        private static void DetectZeroEnergyCycle(string node,
            Dictionary<string, List<(string to, double energy)>> edges,
            HashSet<string> visiting, HashSet<string> done, List<string> stack, CheckResult result)
        {
            if (done.Contains(node) || !edges.ContainsKey(node))
            {
                return;
            }
            if (visiting.Contains(node))
            {
                return;
            }
            visiting.Add(node);
            stack.Add(node);
            foreach (var (to, energy) in edges[node])
            {
                int idx = stack.IndexOf(to);
                if (idx >= 0)
                {
                    if (energy <= 0)
                    {
                        result.Errors.Add("zero-energy production cycle via: " + string.Join(" -> ", stack.Skip(idx)) + " -> " + to);
                    }
                    continue;
                }
                DetectZeroEnergyCycle(to, edges, visiting, done, stack, result);
            }
            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(node);
            done.Add(node);
        }

        private static CheckResult MassConservation(Database db)
        {
            var result = new CheckResult { Name = "5 mass conservation warning (>1.3x gain, non-gas)" };
            foreach (var recipe in db.Recipes)
            {
                if (!TryMass(db, recipe.Inputs, out double inMass) || !TryMass(db, recipe.Outputs, out double outMass))
                {
                    continue;
                }
                bool involvesGas = recipe.Inputs.Concat(recipe.Outputs).Any(i =>
                    db.Items.TryGetValue(i.ItemId, out var item) && item.Tags.Contains("gas"));
                if (!involvesGas && inMass > 0 && outMass > inMass * MassGainWarnFactor)
                {
                    result.Warnings.Add("mass gain " + recipe.Id + ": " + inMass + " -> " + outMass);
                }
            }
            result.Note = "warnings only; whitelist decisions happen in M3 review";
            return result;
        }

        private static bool TryMass(Database db, List<Ingredient> ingredients, out double total)
        {
            total = 0;
            foreach (var ing in ingredients)
            {
                if (!db.Items.TryGetValue(ing.ItemId, out var item) || item.Mass <= 0)
                {
                    return false;
                }
                total += item.Mass * ing.Count;
            }
            return true;
        }

        private static CheckResult UnlockCoverage(Database db)
        {
            var result = new CheckResult { Name = "6 unlock coverage (1 tech node per recipe, <=14 per node)" };
            if (db.TechNodes.Count == 0)
            {
                result.Note = "tech_nodes.csv empty (content arrives with M3-T6); recipes present: " + db.Recipes.Count;
                if (db.Recipes.Count > 0)
                {
                    result.Errors.Add("recipes exist but no tech nodes are defined");
                }
                return result;
            }
            var perNode = new Dictionary<string, int>();
            foreach (var recipe in db.Recipes)
            {
                if (recipe.TechNode.Length == 0)
                {
                    result.Errors.Add("recipe without tech node: " + recipe.Id);
                    continue;
                }
                perNode.TryGetValue(recipe.TechNode, out int count);
                perNode[recipe.TechNode] = count + 1;
            }
            // Tech rows may also reference recipe ids that were never generated (typos).
            foreach (var pair in db.RecipeToTechNode)
            {
                bool found = false;
                foreach (var recipe in db.Recipes)
                {
                    if (recipe.Id == pair.Key)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    result.Errors.Add("tech node " + pair.Value + " references unknown recipe: " + pair.Key);
                }
            }
            foreach (var pair in perNode.Where(p => p.Value > MaxRecipesPerTechNode))
            {
                result.Errors.Add("tech node over recipe budget (" + pair.Value + "/" + MaxRecipesPerTechNode + "): " + pair.Key);
            }
            return result;
        }

        private static CheckResult IconSpecs(Database db)
        {
            var result = new CheckResult { Name = "7 icon spec parseable" };
            foreach (var item in db.Items.Values)
            {
                if (item.IconSpec == "auto" || item.IconSpec.Length == 0)
                {
                    continue;
                }
                string[] parts = item.IconSpec.Split(':');
                if (parts.Length < 2 || parts.Any(p => p.Trim().Length == 0))
                {
                    result.Errors.Add("malformed icon spec on " + item.Id + ": " + item.IconSpec);
                }
            }
            return result;
        }

        private static CheckResult LocalizationKeys(Database db)
        {
            var result = new CheckResult { Name = "8 localization strings present (zh/en)" };
            foreach (var item in db.Items.Values)
            {
                if (item.IsStub)
                {
                    result.Errors.Add("item referenced but never defined: " + item.Id);
                    continue;
                }
                if (item.Zh.Length == 0 || item.En.Length == 0)
                {
                    result.Errors.Add("missing zh/en name: " + item.Id);
                }
            }
            foreach (var recipe in db.Recipes)
            {
                bool matrixFamily = recipe.Family == "A" || recipe.Family == "B";
                if (matrixFamily && (recipe.RationaleZh.Length == 0 || recipe.RationaleEn.Length == 0))
                {
                    result.Errors.Add("matrix recipe missing rationale: " + recipe.Id);
                }
            }
            return result;
        }
    }
}
