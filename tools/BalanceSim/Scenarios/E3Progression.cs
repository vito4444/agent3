using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Scenarios
{
    /// <summary>
    /// E3 progression estimator (docs/plan/09 M3-T7): expands the recipe/tech graph from
    /// the home-planet start to "launch pad unlocked and built", accumulating gathering,
    /// processing and research time on a scripted crew, and reports the top bottleneck
    /// materials. This is a planning-level simulation over the generated catalog (the
    /// full colony sim is exercised by E1/E2); it answers "does the content let a
    /// scripted build order reach T3 in ≤40 game days".
    /// </summary>
    public static class E3Progression
    {
        private const int Miners = 3;
        private const int MachineParallelism = 3;
        private const int Researchers = 1;
        private const float HandFallbackShare = 0.25f;

        public sealed class Result
        {
            public double TotalDays;
            public double GatherDays;
            public double ProcessDays;
            public double ResearchDays;
            public List<(string item, double days)> Bottlenecks = new List<(string, double)>();
        }

        public static Result Run()
        {
            string root = FindRepoRoot();
            var recipes = JObject.Parse(File.ReadAllText(Path.Combine(root, "GeneratedData", "recipes.json")))["recipes"]
                .Cast<JObject>().ToList();
            var items = JObject.Parse(File.ReadAllText(Path.Combine(root, "GeneratedData", "items.json")))["items"]
                .Cast<JObject>().ToDictionary(i => i.Value<string>("Id"), i => i);
            var techNodes = TechSystem.ParseCsv(File.ReadAllLines(Path.Combine(root, "data", "tech_nodes.csv")));

            // Producer index: first (cheapest-by-tick) recipe per item.
            var producer = new Dictionary<string, JObject>();
            foreach (var recipe in recipes)
            {
                foreach (var output in recipe["Outputs"].Cast<JObject>())
                {
                    string id = output.Value<string>("ItemId");
                    if (!producer.TryGetValue(id, out var existing) ||
                        recipe.Value<int>("WorkTicks") < existing.Value<int>("WorkTicks"))
                    {
                        producer[id] = recipe;
                    }
                }
            }

            bool IsRaw(string id) => items.TryGetValue(id, out var item) && item.Value<string>("Category") == "raw";

            var gatherTicks = new Dictionary<string, double>();
            var processTicks = 0.0;
            var visitedStack = new HashSet<string>();

            // Recursively cost one unit of an item (memoized per call by simple recursion;
            // amounts are small enough that exponential blowup is bounded by the tier depth).
            void Need(string itemId, double count, int depth)
            {
                if (depth > 24 || count <= 0)
                {
                    return;
                }
                if (IsRaw(itemId))
                {
                    gatherTicks.TryGetValue(itemId, out double t);
                    gatherTicks[itemId] = t + count * Balance.MineTicksPerUnit;
                    return;
                }
                if (!producer.TryGetValue(itemId, out var recipe) || visitedStack.Contains(itemId))
                {
                    return;
                }
                visitedStack.Add(itemId);
                double outCount = recipe["Outputs"].Cast<JObject>()
                    .First(o => o.Value<string>("ItemId") == itemId).Value<int>("Count");
                double crafts = count / Math.Max(1.0, outCount);
                processTicks += crafts * recipe.Value<int>("WorkTicks") *
                                (1.0 + HandFallbackShare * (Balance.HandcraftTimeFactor - 1.0));
                foreach (var input in recipe["Inputs"].Cast<JObject>())
                {
                    Need(input.Value<string>("ItemId"), crafts * input.Value<int>("Count"), depth + 1);
                }
                visitedStack.Remove(itemId);
            }

            // Research path to launch_infra: all transitive prereqs' core costs.
            var nodesById = techNodes.ToDictionary(n => n.Id, n => n);
            var needed = new HashSet<string>();
            void Walk(string nodeId)
            {
                if (!nodesById.TryGetValue(nodeId, out var node) || !needed.Add(nodeId))
                {
                    return;
                }
                foreach (string prereq in node.Prereqs)
                {
                    Walk(prereq);
                }
            }
            Walk("launch_infra");
            Walk("rocketry_1");

            double researchTicks = 0;
            foreach (string nodeId in needed)
            {
                foreach (var cost in nodesById[nodeId].Cost)
                {
                    researchTicks += cost.Count * Balance.ResearchTicksPerCore;
                    Need(cost.ItemId, cost.Count, 0);
                }
            }

            // Build the launch pad itself plus one rocket's parts.
            foreach (string target in new[] { "launch_pad", "rocket_frame_1", "chem_engine", "fuel_tank_module", "fairing", "nav_pod", "rocket_fuel" })
            {
                Need(target, 1, 0);
            }

            double totalGather = gatherTicks.Values.Sum();
            var result = new Result
            {
                GatherDays = totalGather / Miners / GameConstants.TicksPerDay,
                ProcessDays = processTicks / MachineParallelism / GameConstants.TicksPerDay,
                ResearchDays = researchTicks / Researchers / GameConstants.TicksPerDay
            };
            result.TotalDays = result.GatherDays + result.ProcessDays + result.ResearchDays;
            result.Bottlenecks = gatherTicks
                .OrderByDescending(p => p.Value)
                .Take(5)
                .Select(p => (p.Key, p.Value / Miners / (double)GameConstants.TicksPerDay))
                .ToList();
            return result;
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            for (int depth = 0; depth < 8 && current != null; depth++)
            {
                if (File.Exists(Path.Combine(current.FullName, "data", "materials.csv")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("repo root not found");
        }
    }
}
