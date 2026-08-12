namespace Starsoil.Data
{
    /// <summary>
    /// Locations of RecipeGen outputs (docs/plan/08 data pipeline).
    /// Runtime catalog loading lands with M3 (M3-T2/T3); until then this module
    /// only pins the contract paths shared with tools/RecipeGen.
    /// </summary>
    public static class DataPaths
    {
        public const string GeneratedDataFolder = "GeneratedData";
        public const string RecipesFile = "recipes.json";
        public const string ItemsFile = "items.json";
        public const string ValidationReportFile = "validation_report.md";
    }
}
