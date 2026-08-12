using System;
using System.IO;
using Starsoil.Core;

namespace Starsoil.BalanceSim
{
    /// <summary>Shared helpers for the sim test suite and scenarios.</summary>
    public static class TestUtil
    {
        /// <summary>Locates the repo data/ directory from the test working directory.</summary>
        public static string FindDataDir()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            for (int depth = 0; depth < 8 && current != null; depth++)
            {
                string candidate = Path.Combine(current.FullName, "data");
                if (File.Exists(Path.Combine(candidate, "materials.csv")))
                {
                    return candidate;
                }
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("repo data/ directory not found from " + AppContext.BaseDirectory);
        }

        /// <summary>Loads the generated recipe catalog (checked-in GeneratedData/).</summary>
        public static void LoadTempRecipes(World world)
        {
            string path = Path.Combine(FindDataDir(), "..", "GeneratedData", "recipes.json");
            world.Crafting.LoadRecipes(CatalogRecipes.ParseJson(File.ReadAllText(path)));
        }

        public static void LoadTechTree(World world)
        {
            string path = Path.Combine(FindDataDir(), "tech_nodes.csv");
            world.Tech.LoadNodes(TechSystem.ParseCsv(File.ReadAllLines(path)));
        }

        public static World NewColonyWorld(ulong seed, int size)
        {
            var world = new World(seed, size);
            LoadTempRecipes(world);
            LoadTechTree(world);
            return world;
        }

        /// <summary>Universe with bodies + content loaded (M4 multi-region tests).</summary>
        public static Universe NewUniverse(ulong seed, int size)
        {
            var universe = Universe.NewGame(seed, size);
            universe.Bodies.LoadFromCsv(File.ReadAllLines(Path.Combine(FindDataDir(), "celestial_bodies.csv")));
            string recipesJson = File.ReadAllText(Path.Combine(FindDataDir(), "..", "GeneratedData", "recipes.json"));
            var techNodes = TechSystem.ParseCsv(File.ReadAllLines(Path.Combine(FindDataDir(), "tech_nodes.csv")));
            universe.SetContent(CatalogRecipes.ParseJson(recipesJson), techNodes);
            var pricesRoot = Newtonsoft.Json.Linq.JObject.Parse(
                File.ReadAllText(Path.Combine(FindDataDir(), "..", "GeneratedData", "prices.json")));
            var prices = new System.Collections.Generic.Dictionary<string, double>();
            foreach (var pair in (Newtonsoft.Json.Linq.JObject)pricesRoot["prices"])
            {
                prices[pair.Key] = pair.Value.ToObject<double>();
            }
            universe.SetPrices(prices);
            return universe;
        }

        public static void Run(World world, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.Step();
            }
        }

        /// <summary>Runs until the predicate is true or the tick budget runs out; returns success.</summary>
        public static bool RunUntil(World world, long maxTicks, Func<World, bool> predicate)
        {
            for (long i = 0; i < maxTicks; i++)
            {
                world.Step();
                if (predicate(world))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
