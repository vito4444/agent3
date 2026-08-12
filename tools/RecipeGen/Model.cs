using System.Collections.Generic;

namespace Starsoil.RecipeGen
{
    public sealed class ItemDef
    {
        public string Id;
        public string Zh;
        public string En;
        public string Category;
        public int Tier;
        public List<string> Tags = new List<string>();
        public List<string> SourceBodies = new List<string>();
        public double Mass;
        public string IconSpec = "auto";
        /// <summary>True when the item id was referenced by a recipe but never defined.</summary>
        public bool IsStub;
    }

    public sealed class Ingredient
    {
        public string ItemId;
        public int Count;
    }

    public sealed class RecipeDef
    {
        public string Id;
        public List<Ingredient> Inputs = new List<Ingredient>();
        public List<Ingredient> Outputs = new List<Ingredient>();
        public string Verb;
        public int Tier;
        /// <summary>Count-table family A..H (docs/plan/04).</summary>
        public string Family;
        public double Seconds;
        public double Kw;
        /// <summary>Ticks at machine speed (10 ticks per game-second).</summary>
        public int WorkTicks;
        /// <summary>Machine building id and optional hand station id (runtime bridge).</summary>
        public string MachineStation = string.Empty;
        public string HandStation = string.Empty;
        public string TechNode = string.Empty;
        public string RationaleZh = string.Empty;
        public string RationaleEn = string.Empty;
        public bool FactionLocked;
    }

    public sealed class VerbDef
    {
        public string Id;
        public string Zh;
        public string En;
        public string Machine;
        public string HandVersion;
        public double BaseSeconds;
        public double BaseKw;
        public string TemplateId;
    }

    public sealed class FormDef
    {
        public string Id;
        public string Zh;
        public string En;
        public string Verb;
    }

    public sealed class SmeltableDef
    {
        public string Id;
        public string Zh;
        public string En;
        public int Tier;
        public List<string> Tags = new List<string>();
        public string SourceOre;
        public int OrePerIngot;
    }

    public sealed class TagPhrase
    {
        public string ZhAttr;
        public string ZhUse;
        public string EnAttr;
        public string EnUse;
    }

    public sealed class TechNodeDef
    {
        public string Id;
        public string Zh;
        public string En;
        public string Domain;
        public List<string> Prereqs = new List<string>();
        public List<string> Recipes = new List<string>();
        public List<string> Buildings = new List<string>();
    }

    public sealed class Database
    {
        public readonly Dictionary<string, ItemDef> Items = new Dictionary<string, ItemDef>();
        public readonly List<RecipeDef> Recipes = new List<RecipeDef>();
        public readonly Dictionary<string, VerbDef> Verbs = new Dictionary<string, VerbDef>();
        public readonly Dictionary<string, FormDef> Forms = new Dictionary<string, FormDef>();
        public readonly Dictionary<string, SmeltableDef> Smeltables = new Dictionary<string, SmeltableDef>();
        public readonly Dictionary<string, TagPhrase> TagPhrases = new Dictionary<string, TagPhrase>();
        public readonly Dictionary<string, TechNodeDef> TechNodes = new Dictionary<string, TechNodeDef>();
        public readonly Dictionary<string, string> TemplatesZh = new Dictionary<string, string>();
        public readonly Dictionary<string, string> TemplatesEn = new Dictionary<string, string>();
        /// <summary>node id per recipe id (from tech_nodes.csv recipes columns).</summary>
        public readonly Dictionary<string, string> RecipeToTechNode = new Dictionary<string, string>();

        public ItemDef GetOrStubItem(string id)
        {
            if (!Items.TryGetValue(id, out var item))
            {
                item = new ItemDef { Id = id, Zh = string.Empty, En = string.Empty, Category = "unknown", IsStub = true };
                Items.Add(id, item);
            }
            return item;
        }
    }
}
