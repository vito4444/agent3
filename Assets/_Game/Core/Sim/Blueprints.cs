using System.Collections.Generic;

namespace Starsoil.Core
{
    public sealed class Blueprint
    {
        public int Id;
        public string DefId;
        public int X;
        public int Y;
        public int Rotation;
        /// <summary>Delivered materials, filled by haul tasks.</summary>
        public Inventory Delivered = new Inventory();
        /// <summary>Materials already reserved by in-flight hauls (itemId → count).</summary>
        public Inventory InboundReserved = new Inventory();
        public float BuildProgress;
        public bool MaterialsComplete;
    }

    /// <summary>
    /// Construction flow (docs/plan/03): blueprint → haul materials → build work →
    /// real building. Blueprints occupy their footprint (so nothing overlaps them) via a
    /// shadow reservation grid, not the BuildingSystem occupancy.
    /// </summary>
    public sealed class BlueprintSystem
    {
        private readonly Dictionary<int, Blueprint> _byId = new Dictionary<int, Blueprint>();
        private readonly TerrainGrid _terrain;
        private readonly BuildingSystem _buildings;
        private readonly int[] _reservedCells;
        private int _nextId = 1;

        public BlueprintSystem(TerrainGrid terrain, BuildingSystem buildings)
        {
            _terrain = terrain;
            _buildings = buildings;
            _reservedCells = new int[terrain.Size * terrain.Size];
        }

        public IReadOnlyDictionary<int, Blueprint> All => _byId;

        public bool TryGet(int id, out Blueprint blueprint) => _byId.TryGetValue(id, out blueprint);

        public int GetBlueprintAt(int x, int y)
        {
            if (!_terrain.InBounds(x, y))
            {
                return 0;
            }
            return _reservedCells[y * _terrain.Size + x];
        }

        public PlacementError CanPlace(string defId, int x, int y, int rotation)
        {
            var error = _buildings.CanPlace(defId, x, y, rotation);
            if (error != PlacementError.None)
            {
                return error;
            }
            BuildingDefs.TryGet(defId, out var def);
            BuildingSystem.FootprintSize(def, rotation, out int w, out int h);
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    if (_reservedCells[(y + dy) * _terrain.Size + (x + dx)] != 0)
                    {
                        return PlacementError.Occupied;
                    }
                }
            }
            return PlacementError.None;
        }

        public int Place(string defId, int x, int y, int rotation, out PlacementError error)
        {
            error = CanPlace(defId, x, y, rotation);
            if (error != PlacementError.None)
            {
                return 0;
            }
            var bp = new Blueprint { Id = _nextId, DefId = defId, X = x, Y = y, Rotation = rotation };
            _nextId++;
            _byId.Add(bp.Id, bp);
            MarkCells(bp, bp.Id);
            return bp.Id;
        }

        /// <summary>Cancels a blueprint; delivered materials drop as ground piles.</summary>
        public bool Cancel(int blueprintId, PileSystem piles)
        {
            if (!_byId.TryGetValue(blueprintId, out var bp))
            {
                return false;
            }
            foreach (var entry in bp.Delivered.SortedEntries())
            {
                piles.Drop(entry.Key, entry.Value, bp.X, bp.Y);
            }
            MarkCells(bp, 0);
            _byId.Remove(blueprintId);
            return true;
        }

        /// <summary>Remaining materials still to reserve/haul for this blueprint.</summary>
        public int MissingUnreserved(Blueprint bp, string itemId)
        {
            BuildingDefs.TryGet(bp.DefId, out var def);
            foreach (var need in def.BuildCost)
            {
                if (need.ItemId == itemId)
                {
                    return need.Count - bp.Delivered.Get(itemId) - bp.InboundReserved.Get(itemId);
                }
            }
            return 0;
        }

        public void Deliver(Blueprint bp, string itemId, int count)
        {
            bp.Delivered.Add(itemId, count);
            bp.InboundReserved.Add(itemId, -count);
            BuildingDefs.TryGet(bp.DefId, out var def);
            bool complete = true;
            foreach (var need in def.BuildCost)
            {
                if (bp.Delivered.Get(need.ItemId) < need.Count)
                {
                    complete = false;
                    break;
                }
            }
            bp.MaterialsComplete = complete;
        }

        /// <summary>Applies build work; returns the completed building id, or 0 while in progress.</summary>
        public int ApplyBuildWork(Blueprint bp, float ticks, World world)
        {
            BuildingDefs.TryGet(bp.DefId, out var def);
            bp.BuildProgress += ticks;
            if (bp.BuildProgress < def.BuildWorkTicks)
            {
                return 0;
            }
            MarkCells(bp, 0);
            _byId.Remove(bp.Id);
            int buildingId = world.Buildings.Place(bp.DefId, bp.X, bp.Y, bp.Rotation, out _);
            world.Events.Add(new BuildingPlacedEvent
            {
                BuildingId = buildingId,
                DefId = bp.DefId,
                X = bp.X,
                Y = bp.Y,
                Rotation = bp.Rotation
            });
            world.Stats.CountBuilt(bp.DefId);
            return buildingId;
        }

        private void MarkCells(Blueprint bp, int value)
        {
            BuildingDefs.TryGet(bp.DefId, out var def);
            BuildingSystem.FootprintSize(def, bp.Rotation, out int w, out int h);
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    _reservedCells[(bp.Y + dy) * _terrain.Size + (bp.X + dx)] = value;
                }
            }
        }

        internal void RestoreFrom(List<SavedBlueprint> saved)
        {
            _byId.Clear();
            System.Array.Clear(_reservedCells, 0, _reservedCells.Length);
            _nextId = 1;
            foreach (var s in saved)
            {
                var bp = new Blueprint
                {
                    Id = s.Id,
                    DefId = s.DefId,
                    X = s.X,
                    Y = s.Y,
                    Rotation = s.Rotation,
                    BuildProgress = s.BuildProgress
                };
                if (s.Delivered != null)
                {
                    foreach (var entry in s.Delivered)
                    {
                        bp.Delivered.Add(entry.ItemId, entry.Count);
                    }
                }
                BuildingDefs.TryGet(bp.DefId, out var def);
                bool complete = true;
                foreach (var need in def.BuildCost)
                {
                    if (bp.Delivered.Get(need.ItemId) < need.Count)
                    {
                        complete = false;
                        break;
                    }
                }
                bp.MaterialsComplete = complete;
                _byId.Add(bp.Id, bp);
                MarkCells(bp, bp.Id);
                if (s.Id >= _nextId)
                {
                    _nextId = s.Id + 1;
                }
            }
        }
    }
}
