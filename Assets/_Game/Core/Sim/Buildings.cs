using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    public enum BuildingKind
    {
        Generic,
        CrashPod,
        Workbench,
        Campfire,
        SleepPod,
        Storage,
        HandCrank,
        Road,
        TestBlock
    }

    /// <summary>Immutable building archetype. The T0 set is docs/plan/03; the full catalog
    /// (88 buildings) arrives with the M3 data pipeline.</summary>
    public sealed class BuildingDef
    {
        public string Id { get; }
        public int Width { get; }
        public int Height { get; }
        public BuildingKind Kind { get; }
        /// <summary>Colonists may stand on these cells (roads, pod interiors, beds).</summary>
        public bool Walkable { get; }
        /// <summary>Standing here counts as indoors for needs (docs/plan/02).</summary>
        public bool Interior { get; }
        public int Beds { get; }
        public int StorageCapacity { get; }
        /// <summary>Craft station verbs run here (workbench/campfire at T0).</summary>
        public bool IsStation { get; }
        public IReadOnlyList<Ingredient> BuildCost { get; }
        public int BuildWorkTicks { get; }
        public bool Demolishable { get; }

        public BuildingDef(string id, int width, int height, BuildingKind kind,
            bool walkable = false, bool interior = false, int beds = 0, int storageCapacity = 0,
            bool isStation = false, IReadOnlyList<Ingredient> buildCost = null, int buildWorkTicks = 0,
            bool demolishable = true)
        {
            Id = id;
            Width = width;
            Height = height;
            Kind = kind;
            Walkable = walkable;
            Interior = interior;
            Beds = beds;
            StorageCapacity = storageCapacity;
            IsStation = isStation;
            BuildCost = buildCost ?? Array.Empty<Ingredient>();
            BuildWorkTicks = buildWorkTicks;
            Demolishable = demolishable;
        }
    }

    public static class BuildingDefs
    {
        public const string TestBlockId = "test_block";
        public const string CrashPodId = "crash_pod";
        public const string WorkbenchId = "workbench";
        public const string CampfireId = "campfire";
        public const string SleepPodId = "sleep_pod";
        public const string SmallStorageId = "small_storage";
        public const string HandCrankId = "hand_crank";
        public const string RoadId = "road";

        private const int QuickBuildTicks = 150;
        private const int NormalBuildTicks = 300;
        private const int SmallStorageStacks = 24;

        private static Ingredient Need(string itemId, int count) => new Ingredient { ItemId = itemId, Count = count };

        public static readonly BuildingDef TestBlock =
            new BuildingDef(TestBlockId, 2, 2, BuildingKind.TestBlock,
                buildCost: Array.Empty<Ingredient>(), buildWorkTicks: QuickBuildTicks);

        public static readonly BuildingDef CrashPod =
            new BuildingDef(CrashPodId, 3, 3, BuildingKind.CrashPod,
                walkable: true, interior: true, beds: Balance.PodBeds, storageCapacity: Balance.PodStorageCapacity,
                demolishable: false);

        public static readonly BuildingDef Workbench =
            new BuildingDef(WorkbenchId, 2, 1, BuildingKind.Workbench, isStation: true,
                buildCost: new[] { Need(ItemIds.IronOre, 3), Need(ItemIds.Carbon, 1) },
                buildWorkTicks: NormalBuildTicks);

        public static readonly BuildingDef Campfire =
            new BuildingDef(CampfireId, 1, 1, BuildingKind.Campfire, isStation: true,
                buildCost: new[] { Need(ItemIds.Carbon, 2) },
                buildWorkTicks: QuickBuildTicks);

        public static readonly BuildingDef SleepPod =
            new BuildingDef(SleepPodId, 1, 2, BuildingKind.SleepPod, walkable: true, interior: true, beds: 1,
                buildCost: new[] { Need(ItemIds.Fiber, 2), Need(ItemIds.InsulationWrap, 1), Need(ItemIds.CrudeGlass, 1) },
                buildWorkTicks: NormalBuildTicks);

        public static readonly BuildingDef SmallStorage =
            new BuildingDef(SmallStorageId, 2, 2, BuildingKind.Storage, storageCapacity: SmallStorageStacks * Balance.PileMaxStack,
                buildCost: new[] { Need(ItemIds.Fiber, 4), Need(ItemIds.CrudeTool, 1) },
                buildWorkTicks: NormalBuildTicks);

        public static readonly BuildingDef HandCrank =
            new BuildingDef(HandCrankId, 1, 1, BuildingKind.HandCrank,
                buildCost: new[] { Need(ItemIds.IronLump, 2), Need(ItemIds.CopperLump, 1), Need(ItemIds.CrudeTool, 1) },
                buildWorkTicks: NormalBuildTicks);

        public static readonly BuildingDef Road =
            new BuildingDef(RoadId, 1, 1, BuildingKind.Road, walkable: true,
                buildCost: new[] { Need(ItemIds.QuartzSand, 1) },
                buildWorkTicks: QuickBuildTicks / 3);

        private static readonly Dictionary<string, BuildingDef> ById = new Dictionary<string, BuildingDef>
        {
            { TestBlock.Id, TestBlock },
            { CrashPod.Id, CrashPod },
            { Workbench.Id, Workbench },
            { Campfire.Id, Campfire },
            { SleepPod.Id, SleepPod },
            { SmallStorage.Id, SmallStorage },
            { HandCrank.Id, HandCrank },
            { Road.Id, Road }
        };

        /// <summary>Buildable at T0 through the M1 build menu (crash pod is pre-placed).</summary>
        public static readonly string[] BuildableT0 =
        {
            CampfireId, WorkbenchId, SleepPodId, SmallStorageId, HandCrankId, RoadId
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
        /// <summary>0-100; storms wear exposed stations down (docs/plan/02 hazards).</summary>
        public float Durability = Balance.NeedMax;
        /// <summary>Hand crank staffing toggle.</summary>
        public bool StaffedRequested;
        /// <summary>Station input/output buffer or storage contents.</summary>
        public Inventory Stock = new Inventory();
        /// <summary>Outbound reservations against Stock (this building as a haul source).</summary>
        public Inventory Reserved = new Inventory();
        /// <summary>Inbound in-flight deliveries (this station as a haul destination).</summary>
        public Inventory Inbound = new Inventory();
        /// <summary>Colonist id currently occupying the bed (sleep pods / crash pod slots).</summary>
        public List<int> SleepersIds = new List<int>();
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

        /// <summary>Places a completed building; returns its id, or 0 with an error when invalid.</summary>
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

        public bool IsCellWalkable(int x, int y)
        {
            int id = GetBuildingAt(x, y);
            if (id == 0)
            {
                return true;
            }
            return _byId.TryGetValue(id, out var state) &&
                   BuildingDefs.TryGet(state.DefId, out var def) && def.Walkable;
        }

        public bool IsCellInterior(int x, int y)
        {
            int id = GetBuildingAt(x, y);
            return id != 0 && _byId.TryGetValue(id, out var state) &&
                   BuildingDefs.TryGet(state.DefId, out var def) && def.Interior;
        }

        public bool IsCellRoad(int x, int y)
        {
            int id = GetBuildingAt(x, y);
            return id != 0 && _byId.TryGetValue(id, out var state) && state.DefId == BuildingDefs.RoadId;
        }

        public BuildingState FindFirstOfKind(BuildingKind kind)
        {
            BuildingState best = null;
            foreach (var state in _byId.Values)
            {
                if (BuildingDefs.TryGet(state.DefId, out var def) && def.Kind == kind &&
                    (best == null || state.Id < best.Id))
                {
                    best = state;
                }
            }
            return best;
        }

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

        internal void RestoreFrom(List<SavedBuilding> saved)
        {
            _byId.Clear();
            Array.Clear(_occupancy, 0, _occupancy.Length);
            _nextId = 1;
            foreach (var s in saved)
            {
                var state = new BuildingState
                {
                    Id = s.Id,
                    DefId = s.DefId,
                    X = s.X,
                    Y = s.Y,
                    Rotation = s.Rotation,
                    Durability = s.Durability,
                    StaffedRequested = s.StaffedRequested
                };
                if (s.Stock != null)
                {
                    foreach (var entry in s.Stock)
                    {
                        state.Stock.Add(entry.ItemId, entry.Count);
                    }
                }
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
