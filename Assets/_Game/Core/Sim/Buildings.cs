using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    /// <summary>Immutable building archetype. Full defs arrive with the M3 data pipeline;
    /// M0 ships only the test block.</summary>
    public sealed class BuildingDef
    {
        public string Id { get; }
        public int Width { get; }
        public int Height { get; }

        public BuildingDef(string id, int width, int height)
        {
            Id = id;
            Width = width;
            Height = height;
        }
    }

    public static class BuildingDefs
    {
        public const string TestBlockId = "test_block";

        public static readonly BuildingDef TestBlock = new BuildingDef(TestBlockId, 2, 2);

        private static readonly Dictionary<string, BuildingDef> ById = new Dictionary<string, BuildingDef>
        {
            { TestBlock.Id, TestBlock }
        };

        public static bool TryGet(string id, out BuildingDef def) => ById.TryGetValue(id, out def);
    }

    public enum PlacementError
    {
        None,
        UnknownDef,
        OutOfBounds,
        NotFlat,
        Occupied
    }

    public sealed class BuildingState
    {
        public int Id;
        public string DefId;
        public int X;
        public int Y;
        /// <summary>Quarter turns, 0..3.</summary>
        public int Rotation;
    }

    /// <summary>
    /// Building placement/removal rules (docs/plan/03): footprints must be in bounds,
    /// on flat same-height ground, and not overlap. Cells track the occupying building id.
    /// </summary>
    public sealed class BuildingSystem
    {
        private const int RotationCount = 4;

        private readonly TerrainGrid _terrain;
        private readonly Dictionary<int, BuildingState> _byId = new Dictionary<int, BuildingState>();
        private readonly int[] _occupancy;
        private int _nextId = 1;

        public BuildingSystem(TerrainGrid terrain)
        {
            _terrain = terrain;
            _occupancy = new int[terrain.Size * terrain.Size];
        }

        public IReadOnlyDictionary<int, BuildingState> All => _byId;

        public int Count => _byId.Count;

        public static void FootprintSize(BuildingDef def, int rotation, out int w, out int h)
        {
            bool swapped = (rotation & 1) == 1;
            w = swapped ? def.Height : def.Width;
            h = swapped ? def.Width : def.Height;
        }

        public PlacementError CanPlace(string defId, int x, int y, int rotation)
        {
            if (!BuildingDefs.TryGet(defId, out var def))
            {
                return PlacementError.UnknownDef;
            }
            int rot = ((rotation % RotationCount) + RotationCount) % RotationCount;
            FootprintSize(def, rot, out int w, out int h);

            if (!_terrain.InBounds(x, y) || !_terrain.InBounds(x + w - 1, y + h - 1))
            {
                return PlacementError.OutOfBounds;
            }

            int baseHeight = _terrain.GetHeight(x, y);
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    if (_terrain.GetHeight(x + dx, y + dy) != baseHeight)
                    {
                        return PlacementError.NotFlat;
                    }
                    if (_occupancy[(y + dy) * _terrain.Size + (x + dx)] != 0)
                    {
                        return PlacementError.Occupied;
                    }
                }
            }
            return PlacementError.None;
        }

        /// <summary>Places a building; returns its id, or 0 with an error when invalid.</summary>
        public int Place(string defId, int x, int y, int rotation, out PlacementError error)
        {
            error = CanPlace(defId, x, y, rotation);
            if (error != PlacementError.None)
            {
                return 0;
            }
            int rot = ((rotation % RotationCount) + RotationCount) % RotationCount;
            var state = new BuildingState { Id = _nextId, DefId = defId, X = x, Y = y, Rotation = rot };
            _nextId++;
            _byId.Add(state.Id, state);
            Occupy(state, state.Id);
            return state.Id;
        }

        public bool Remove(int buildingId)
        {
            if (!_byId.TryGetValue(buildingId, out var state))
            {
                return false;
            }
            Occupy(state, 0);
            _byId.Remove(buildingId);
            return true;
        }

        /// <summary>Returns the building id occupying the cell, or 0.</summary>
        public int GetBuildingAt(int x, int y)
        {
            if (!_terrain.InBounds(x, y))
            {
                return 0;
            }
            return _occupancy[y * _terrain.Size + x];
        }

        public bool TryGet(int buildingId, out BuildingState state) => _byId.TryGetValue(buildingId, out state);

        private void Occupy(BuildingState state, int value)
        {
            BuildingDefs.TryGet(state.DefId, out var def);
            FootprintSize(def, state.Rotation, out int w, out int h);
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    _occupancy[(state.Y + dy) * _terrain.Size + (state.X + dx)] = value;
                }
            }
        }

        public void WriteTo(ref StateHasher hasher)
        {
            hasher.Add(_byId.Count);
            var ids = new List<int>(_byId.Keys);
            ids.Sort();
            for (int i = 0; i < ids.Count; i++)
            {
                var b = _byId[ids[i]];
                hasher.Add(b.Id);
                hasher.Add(b.DefId);
                hasher.Add(b.X);
                hasher.Add(b.Y);
                hasher.Add(b.Rotation);
            }
        }

        internal void RestoreFrom(List<SavedBuilding> saved)
        {
            _byId.Clear();
            Array.Clear(_occupancy, 0, _occupancy.Length);
            _nextId = 1;
            for (int i = 0; i < saved.Count; i++)
            {
                var s = saved[i];
                var state = new BuildingState { Id = s.Id, DefId = s.DefId, X = s.X, Y = s.Y, Rotation = s.Rotation };
                _byId.Add(state.Id, state);
                Occupy(state, state.Id);
                if (s.Id >= _nextId)
                {
                    _nextId = s.Id + 1;
                }
            }
        }
    }
}
