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
                if (File.Exists(Path.Combine(candidate, "temp_recipes_m1.csv")))
                {
                    return candidate;
                }
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("repo data/ directory not found from " + AppContext.BaseDirectory);
        }

        public static void LoadTempRecipes(World world)
        {
            string path = Path.Combine(FindDataDir(), "temp_recipes_m1.csv");
            world.Crafting.LoadRecipes(CraftingSystem.ParseTempRecipesCsv(File.ReadAllLines(path)));
        }

        public static World NewColonyWorld(ulong seed, int size)
        {
            var world = new World(seed, size);
            LoadTempRecipes(world);
            return world;
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
