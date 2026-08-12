using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Starsoil.RecipeGen
{
    /// <summary>
    /// Content pipeline CLI (docs/plan/04): reads hand-maintained CSVs from data/,
    /// generates GeneratedData/recipes.json + items.json + validation_report.md.
    /// `--generate` writes outputs; `--validate` also exits non-zero on any check error
    /// (CI gate 1 hook, see .github/workflows/ci.yml).
    /// </summary>
    public static class Program
    {
        private const int ExitOk = 0;
        private const int ExitBadArgs = 1;
        private const int ExitValidationFailed = 2;
        private const int SchemaVersion = 0;

        public static int Main(string[] args)
        {
            bool generate = args.Contains("--generate");
            bool validate = args.Contains("--validate");
            if (!generate && !validate)
            {
                Console.WriteLine("usage: RecipeGen --generate|--validate [--data <dir>] [--out <dir>]");
                return ExitBadArgs;
            }

            string dataDir = OptionValue(args, "--data") ?? FindDirUpwards("data");
            string outDir = OptionValue(args, "--out") ?? Path.Combine(Path.GetDirectoryName(dataDir) ?? ".", "GeneratedData");

            var db = Loader.Load(dataDir);
            Generator.Run(db, dataDir);
            var checks = Checks.RunAll(db);

            Directory.CreateDirectory(outDir);
            WriteJson(Path.Combine(outDir, "items.json"), new { schemaVersion = SchemaVersion, items = db.Items.Values.OrderBy(i => i.Id, StringComparer.Ordinal) });
            var prices = Prices.Solve(db);
            WriteJson(Path.Combine(outDir, "prices.json"), new
            {
                schemaVersion = SchemaVersion,
                prices = prices.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .ToDictionary(p => p.Key, p => Math.Round(p.Value, 2))
            });
            WriteJson(Path.Combine(outDir, "recipes.json"), new { schemaVersion = SchemaVersion, recipes = db.Recipes.OrderBy(r => r.Id, StringComparer.Ordinal) });
            string report = BuildReport(db, checks);
            File.WriteAllText(Path.Combine(outDir, "validation_report.md"), report, new UTF8Encoding(false));

            int errorCount = checks.Sum(c => c.Errors.Count);
            int warningCount = checks.Sum(c => c.Warnings.Count);
            Console.WriteLine("RecipeGen: " + db.Recipes.Count + " recipes, " + db.Items.Count + " items, " +
                              errorCount + " errors, " + warningCount + " warnings");
            Console.WriteLine("outputs -> " + Path.GetFullPath(outDir));

            if (validate && errorCount > 0)
            {
                foreach (var check in checks.Where(c => !c.Passed))
                {
                    Console.Error.WriteLine("[FAIL] " + check.Name);
                    foreach (string error in check.Errors)
                    {
                        Console.Error.WriteLine("  - " + error);
                    }
                }
                return ExitValidationFailed;
            }
            return ExitOk;
        }

        private static string OptionValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        /// <summary>Finds a directory by walking up from the CWD so the tool runs from repo root or tools/.</summary>
        private static string FindDirUpwards(string dirName)
        {
            var current = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int depth = 0; depth < 6 && current != null; depth++)
            {
                string candidate = Path.Combine(current.FullName, dirName);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                current = current.Parent;
            }
            return dirName;
        }

        private static void WriteJson(string path, object payload)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(payload, Formatting.Indented), new UTF8Encoding(false));
        }

        private static string BuildReport(Database db, List<CheckResult> checks)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# RecipeGen validation report");
            sb.AppendLine();
            sb.AppendLine("Generated at (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            sb.AppendLine("## Counts by family (target table: docs/plan/04)");
            sb.AppendLine();
            sb.AppendLine("| family | recipes |");
            sb.AppendLine("|---|---|");
            foreach (string family in new[] { "A", "B", "C", "D", "E", "F", "G", "H" })
            {
                sb.AppendLine("| " + family + " | " + db.Recipes.Count(r => r.Family == family) + " |");
            }
            sb.AppendLine("| total | " + db.Recipes.Count + " |");
            sb.AppendLine();
            sb.AppendLine("Items: " + db.Items.Count + " · Verbs: " + db.Verbs.Count + " · Forms: " + db.Forms.Count +
                          " · Tech nodes: " + db.TechNodes.Count);
            sb.AppendLine();

            sb.AppendLine("## Checks");
            sb.AppendLine();
            foreach (var check in checks)
            {
                sb.AppendLine("### " + check.Name + " — " + (check.Passed ? "PASS" : "FAIL"));
                if (check.Note.Length > 0)
                {
                    sb.AppendLine("_" + check.Note + "_");
                }
                foreach (string error in check.Errors)
                {
                    sb.AppendLine("- ERROR: " + error);
                }
                foreach (string warning in check.Warnings)
                {
                    sb.AppendLine("- warning: " + warning);
                }
                sb.AppendLine();
            }

            int errorCount = checks.Sum(c => c.Errors.Count);
            int warningCount = checks.Sum(c => c.Warnings.Count);
            sb.AppendLine("TOTAL recipes: " + db.Recipes.Count + ", errors: " + errorCount + ", warnings: " + warningCount);
            return sb.ToString();
        }
    }
}
