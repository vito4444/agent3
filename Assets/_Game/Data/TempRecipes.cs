using System.IO;
using UnityEngine;
using Starsoil.Core;

namespace Starsoil.Data
{
    /// <summary>
    /// Loads the M1 temporary hand recipes into a world (staged interface; the RecipeGen
    /// catalog replaces this at M3-T10, see docs/plan/09).
    /// </summary>
    public static class TempRecipes
    {
        public static bool LoadInto(World world)
        {
            string path = DataFiles.RepoDataPath("temp_recipes_m1.csv");
            if (path == null || !File.Exists(path))
            {
                Debug.LogError("[TempRecipes] data/temp_recipes_m1.csv not found; crafting will be empty.");
                return false;
            }
            world.Crafting.LoadRecipes(CraftingSystem.ParseTempRecipesCsv(File.ReadAllLines(path)));
            return true;
        }
    }
}
