using System.Collections.Generic;

namespace Starsoil.Core
{
    /// <summary>M1 item id constants. Display names live in data/localization.csv (key item_&lt;id&gt;).</summary>
    public static class ItemIds
    {
        public const string Ice = "ice";
        public const string Water = "water";
        public const string Biomass = "biomass";
        public const string Fiber = "fiber";
        public const string Ration = "ration";
        public const string PreservedRation = "preserved_ration";
        public const string IronOre = "iron_ore";
        public const string IronLump = "iron_lump";
        public const string CopperOre = "copper_ore";
        public const string CopperLump = "copper_lump";
        public const string QuartzSand = "quartz_sand";
        public const string CrudeGlass = "crude_glass";
        public const string SaltOre = "salt_ore";
        public const string Salt = "salt";
        public const string Carbon = "carbon";
        public const string CarbonPowder = "carbon_powder";
        public const string InsulationWrap = "insulation_wrap";
        public const string CrudeTool = "crude_tool";
        public const string Bandage = "bandage";
        public const string OxygenBottle = "oxygen_bottle";
        public const string AlgaeSeed = "algae_seed";
        public const string Remains = "remains";

        /// <summary>Items that satisfy the food need, in preference order.</summary>
        public static readonly string[] Foods = { Ration, PreservedRation };
    }

    /// <summary>Item quantity pair used by recipes and build costs.</summary>
    public sealed class Ingredient
    {
        public string ItemId;
        public int Count;
    }

    /// <summary>Deterministic item→count map (keys sorted on capture/hash).</summary>
    public sealed class Inventory
    {
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        public IReadOnlyDictionary<string, int> Counts => _counts;

        public int Get(string itemId) => _counts.TryGetValue(itemId, out int v) ? v : 0;

        public void Add(string itemId, int count)
        {
            if (count == 0)
            {
                return;
            }
            int next = Get(itemId) + count;
            if (next <= 0)
            {
                _counts.Remove(itemId);
            }
            else
            {
                _counts[itemId] = next;
            }
        }

        public bool TryRemove(string itemId, int count)
        {
            if (Get(itemId) < count)
            {
                return false;
            }
            Add(itemId, -count);
            return true;
        }

        public int TotalUnits()
        {
            int total = 0;
            foreach (var pair in _counts)
            {
                total += pair.Value;
            }
            return total;
        }

        public List<KeyValuePair<string, int>> SortedEntries()
        {
            var list = new List<KeyValuePair<string, int>>(_counts);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }
    }

    public sealed class GroundPile
    {
        public int Id;
        public string ItemId;
        public int Count;
        public int Reserved;
        public int X;
        public int Y;

        public int Available => Count - Reserved;
    }

    /// <summary>Loose items on the ground. Piles merge per cell+item and vanish at zero.</summary>
    public sealed class PileSystem
    {
        private readonly Dictionary<int, GroundPile> _piles = new Dictionary<int, GroundPile>();
        private int _nextId = 1;

        public IReadOnlyDictionary<int, GroundPile> All => _piles;

        public GroundPile Drop(string itemId, int count, int x, int y)
        {
            foreach (var pile in _piles.Values)
            {
                if (pile.X == x && pile.Y == y && pile.ItemId == itemId && pile.Count + count <= Balance.PileMaxStack)
                {
                    pile.Count += count;
                    return pile;
                }
            }
            var created = new GroundPile { Id = _nextId, ItemId = itemId, Count = count, X = x, Y = y };
            _nextId++;
            _piles.Add(created.Id, created);
            return created;
        }

        public bool TryGet(int id, out GroundPile pile) => _piles.TryGetValue(id, out pile);

        public void Take(GroundPile pile, int count)
        {
            pile.Count -= count;
            if (pile.Reserved > pile.Count)
            {
                pile.Reserved = pile.Count;
            }
            if (pile.Count <= 0)
            {
                _piles.Remove(pile.Id);
            }
        }

        public int CountOf(string itemId)
        {
            int total = 0;
            foreach (var pile in _piles.Values)
            {
                if (pile.ItemId == itemId)
                {
                    total += pile.Count;
                }
            }
            return total;
        }

        internal void RestoreFrom(List<SavedPile> saved)
        {
            _piles.Clear();
            _nextId = 1;
            foreach (var s in saved)
            {
                _piles.Add(s.Id, new GroundPile { Id = s.Id, ItemId = s.ItemId, Count = s.Count, X = s.X, Y = s.Y });
                if (s.Id >= _nextId)
                {
                    _nextId = s.Id + 1;
                }
            }
        }
    }
}
