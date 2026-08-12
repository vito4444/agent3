using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    public sealed class ResourceNode
    {
        public int Id;
        /// <summary>Item produced per mining cycle.</summary>
        public string ItemId;
        public int Remaining;
        public int X;
        public int Y;
        /// <summary>Player toggled this node for harvesting (M1-T4 designation).</summary>
        public bool Designated;
        /// <summary>Ticks of hand work per yielded unit.</summary>
        public int TicksPerUnit;
        public int ClaimedBy;

        public bool IsShrub => ItemId == ItemIds.Biomass;
    }

    /// <summary>
    /// Resource nodes with the start-region guarantees from docs/plan/02: the six starter
    /// deposits within 60 cells of the crash site, ≥6 biomass shrubs near the start and
    /// a wider scatter across the region. Nodes never block movement or building.
    /// </summary>
    public sealed class NodeSystem
    {
        private static readonly string[] GuaranteedDeposits =
        {
            ItemIds.IronOre, ItemIds.CopperOre, ItemIds.QuartzSand,
            ItemIds.SaltOre, ItemIds.Carbon, ItemIds.Ice
        };

        private readonly Dictionary<int, ResourceNode> _nodes = new Dictionary<int, ResourceNode>();
        private int _nextId = 1;

        public IReadOnlyDictionary<int, ResourceNode> All => _nodes;

        public bool TryGet(int id, out ResourceNode node) => _nodes.TryGetValue(id, out node);

        public void Generate(TerrainGrid terrain, BuildingSystem buildings, int startX, int startY, Rng stream)
        {
            foreach (string itemId in GuaranteedDeposits)
            {
                var cell = FindFreeCellNear(terrain, buildings, startX, startY, Balance.GuaranteedDepositRadius, stream);
                Spawn(itemId, stream.NextInt(Balance.DepositMinAmount, Balance.DepositMaxAmount + 1),
                    cell.x, cell.y, Balance.MineTicksPerUnit);
            }

            for (int i = 0; i < Balance.ExtraDepositCount; i++)
            {
                string itemId = GuaranteedDeposits[stream.NextInt(0, GuaranteedDeposits.Length)];
                var cell = FindFreeCellNear(terrain, buildings, terrain.Size / 2, terrain.Size / 2, terrain.Size / 2, stream);
                Spawn(itemId, stream.NextInt(Balance.DepositMinAmount, Balance.DepositMaxAmount + 1),
                    cell.x, cell.y, Balance.MineTicksPerUnit);
            }

            for (int i = 0; i < Balance.MinShrubsNearStart; i++)
            {
                var cell = FindFreeCellNear(terrain, buildings, startX, startY, Balance.GuaranteedDepositRadius, stream);
                Spawn(ItemIds.Biomass, stream.NextInt(Balance.ShrubMinAmount, Balance.ShrubMaxAmount + 1),
                    cell.x, cell.y, Balance.GatherTicksPerUnit);
            }
            for (int i = Balance.MinShrubsNearStart; i < Balance.TotalShrubs; i++)
            {
                var cell = FindFreeCellNear(terrain, buildings, terrain.Size / 2, terrain.Size / 2, terrain.Size / 2, stream);
                Spawn(ItemIds.Biomass, stream.NextInt(Balance.ShrubMinAmount, Balance.ShrubMaxAmount + 1),
                    cell.x, cell.y, Balance.GatherTicksPerUnit);
            }
        }

        private (int x, int y) FindFreeCellNear(TerrainGrid terrain, BuildingSystem buildings,
            int centerX, int centerY, int radius, Rng stream)
        {
            const int maxAttempts = 400;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                int x = centerX + stream.NextInt(-radius, radius + 1);
                int y = centerY + stream.NextInt(-radius, radius + 1);
                if (!terrain.InBounds(x, y) || buildings.GetBuildingAt(x, y) != 0 || GetNodeAt(x, y) != 0)
                {
                    continue;
                }
                int dx = Math.Abs(x - centerX);
                int dy = Math.Abs(y - centerY);
                if (dx + dy > radius)
                {
                    continue;
                }
                return (x, y);
            }
            return (centerX, centerY);
        }

        public ResourceNode Spawn(string itemId, int amount, int x, int y, int ticksPerUnit)
        {
            var node = new ResourceNode
            {
                Id = _nextId,
                ItemId = itemId,
                Remaining = amount,
                X = x,
                Y = y,
                TicksPerUnit = ticksPerUnit
            };
            _nextId++;
            _nodes.Add(node.Id, node);
            return node;
        }

        /// <summary>Removes one unit; returns false when the node is depleted and removed.</summary>
        public bool ExtractUnit(ResourceNode node)
        {
            node.Remaining--;
            if (node.Remaining <= 0)
            {
                _nodes.Remove(node.Id);
                return false;
            }
            return true;
        }

        public int GetNodeAt(int x, int y)
        {
            foreach (var node in _nodes.Values)
            {
                if (node.X == x && node.Y == y)
                {
                    return node.Id;
                }
            }
            return 0;
        }

        public int CountWithin(string itemId, int centerX, int centerY, int radius)
        {
            int count = 0;
            foreach (var node in _nodes.Values)
            {
                if (node.ItemId == itemId &&
                    Math.Abs(node.X - centerX) + Math.Abs(node.Y - centerY) <= radius)
                {
                    count++;
                }
            }
            return count;
        }

        internal void RestoreFrom(List<SavedNode> saved)
        {
            _nodes.Clear();
            _nextId = 1;
            foreach (var s in saved)
            {
                _nodes.Add(s.Id, new ResourceNode
                {
                    Id = s.Id,
                    ItemId = s.ItemId,
                    Remaining = s.Remaining,
                    X = s.X,
                    Y = s.Y,
                    Designated = s.Designated,
                    TicksPerUnit = s.TicksPerUnit
                });
                if (s.Id >= _nextId)
                {
                    _nextId = s.Id + 1;
                }
            }
        }
    }
}
