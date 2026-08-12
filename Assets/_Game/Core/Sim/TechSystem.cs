using System;
using System.Collections.Generic;
using System.Globalization;

namespace Starsoil.Core
{
    public sealed class TechNode
    {
        public string Id;
        public string Zh;
        public string En;
        public string Domain;
        public List<string> Prereqs = new List<string>();
        /// <summary>Data-core costs (docs/plan/04). Empty cost = unlocked from the start.</summary>
        public List<Ingredient> Cost = new List<Ingredient>();
        public List<string> Recipes = new List<string>();
        public List<string> Buildings = new List<string>();
    }

    /// <summary>
    /// Tech tree v1 (M2-T11): nodes load from data/tech_nodes.csv, zero-cost nodes are
    /// unlocked at start, research consumes data cores at the research bench, and
    /// unlocks gate blueprints and craft orders. The full 90-node tree lands at M3-T6.
    /// </summary>
    public sealed class TechSystem
    {
        private readonly Dictionary<string, TechNode> _nodes = new Dictionary<string, TechNode>();
        private readonly Dictionary<string, string> _recipeToNode = new Dictionary<string, string>();
        private readonly HashSet<string> _unlocked = new HashSet<string>();

        public string ResearchTarget = string.Empty;
        public Inventory PaidCores = new Inventory();

        public IReadOnlyDictionary<string, TechNode> Nodes => _nodes;

        public IReadOnlyCollection<string> Unlocked => _unlocked;

        /// <summary>Loads node definitions. Already-unlocked ids are preserved (loading
        /// happens after save restore), zero-cost nodes always unlock.</summary>
        public void LoadNodes(IEnumerable<TechNode> nodes)
        {
            _nodes.Clear();
            _recipeToNode.Clear();
            foreach (var node in nodes)
            {
                _nodes[node.Id] = node;
                foreach (string recipe in node.Recipes)
                {
                    _recipeToNode[recipe] = node.Id;
                }
                if (node.Cost.Count == 0)
                {
                    _unlocked.Add(node.Id);
                }
            }
        }

        public bool IsUnlocked(string nodeId) => _unlocked.Contains(nodeId);

        public bool IsBuildingUnlocked(BuildingDef def)
        {
            if (string.IsNullOrEmpty(def.TechNode))
            {
                return true;
            }
            return _unlocked.Contains(def.TechNode);
        }

        public bool IsRecipeUnlocked(string recipeId)
        {
            if (!_recipeToNode.TryGetValue(recipeId, out string nodeId))
            {
                return true;
            }
            return _unlocked.Contains(nodeId);
        }

        public bool CanSelectTarget(string nodeId)
        {
            if (!_nodes.TryGetValue(nodeId, out var node) || _unlocked.Contains(nodeId))
            {
                return false;
            }
            foreach (string prereq in node.Prereqs)
            {
                if (!_unlocked.Contains(prereq))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Core units still owed for the current target.</summary>
        public int RemainingCost(string itemId)
        {
            if (!_nodes.TryGetValue(ResearchTarget, out var node))
            {
                return 0;
            }
            foreach (var cost in node.Cost)
            {
                if (cost.ItemId == itemId)
                {
                    return Math.Max(0, cost.Count - PaidCores.Get(itemId));
                }
            }
            return 0;
        }

        /// <summary>First core item still owed, or null when the target is fully paid.</summary>
        public string NextNeededCore()
        {
            if (!_nodes.TryGetValue(ResearchTarget, out var node))
            {
                return null;
            }
            foreach (var cost in node.Cost)
            {
                if (PaidCores.Get(cost.ItemId) < cost.Count)
                {
                    return cost.ItemId;
                }
            }
            return null;
        }

        /// <summary>Registers a consumed core; unlocks the node when the bill is settled.</summary>
        public void PayCore(World world, string itemId)
        {
            PaidCores.Add(itemId, 1);
            if (NextNeededCore() == null && _nodes.ContainsKey(ResearchTarget))
            {
                _unlocked.Add(ResearchTarget);
                world.Events.Add(new ResearchCompletedEvent { NodeId = ResearchTarget });
                ResearchTarget = string.Empty;
                PaidCores = new Inventory();
            }
        }

        /// <summary>Test/scenario helper: everything known (pre-built bases, E2).</summary>
        public void UnlockAll()
        {
            foreach (var node in _nodes.Keys)
            {
                _unlocked.Add(node);
            }
        }

        internal void RestoreUnlocked(List<string> unlocked, string target, List<SavedStack> paid)
        {
            _unlocked.Clear();
            if (unlocked != null)
            {
                foreach (string id in unlocked)
                {
                    _unlocked.Add(id);
                }
            }
            ResearchTarget = target ?? string.Empty;
            PaidCores = new Inventory();
            if (paid != null)
            {
                foreach (var stack in paid)
                {
                    PaidCores.Add(stack.ItemId, stack.Count);
                }
            }
        }

        /// <summary>Parses data/tech_nodes.csv lines (id,zh,en,domain,prereqs,cost,recipes,buildings).</summary>
        public static List<TechNode> ParseCsv(IEnumerable<string> lines)
        {
            var result = new List<TechNode>();
            string[] header = null;
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                var cells = SplitCsv(line);
                if (header == null)
                {
                    header = cells.ToArray();
                    continue;
                }
                string Get(string column)
                {
                    for (int i = 0; i < header.Length && i < cells.Count; i++)
                    {
                        if (string.Equals(header[i].Trim(), column, StringComparison.OrdinalIgnoreCase))
                        {
                            return cells[i].Trim();
                        }
                    }
                    return string.Empty;
                }

                string id = Get("id");
                if (id.Length == 0)
                {
                    continue;
                }
                var node = new TechNode
                {
                    Id = id,
                    Zh = Get("zh"),
                    En = Get("en"),
                    Domain = Get("domain"),
                    Prereqs = SplitMulti(Get("prereqs")),
                    Recipes = SplitMulti(Get("recipes")),
                    Buildings = SplitMulti(Get("buildings"))
                };
                foreach (string entry in SplitMulti(Get("cost")))
                {
                    string[] parts = entry.Split(':');
                    int count = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 1;
                    node.Cost.Add(new Ingredient { ItemId = parts[0].Trim(), Count = count });
                }
                result.Add(node);
            }
            return result;
        }

        private static List<string> SplitMulti(string cell)
        {
            var list = new List<string>();
            foreach (string part in cell.Split(';'))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    list.Add(trimmed);
                }
            }
            return list;
        }

        private static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            var current = new System.Text.StringBuilder();
            bool quoted = false;
            foreach (char c in line)
            {
                if (quoted)
                {
                    if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    cells.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            cells.Add(current.ToString());
            return cells;
        }
    }
}
