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
        TestBlock,
        // M2 power.
        SolarPanel,
        WindTurbine,
        Battery,
        PowerPylon,
        // M2 oxygen.
        GasPylon,
        GasTank,
        Electrolyzer,
        AirChargingStation,
        // M2 machines (docs/plan/03 machine families).
        Miner,
        IceMiner,
        Crusher,
        Furnace,
        RollMill,
        Press,
        Assembler,
        WaterPurifier,
        Greenhouse,
        ForageStation,
        BotStation,
        ChargingPost,
        ResearchBench
    }

    /// <summary>Immutable building archetype. The T0/T1 set covers docs/plan/03 categories
    /// needed by M1-M2; the full 88-building catalog arrives with the M3 data pipeline.</summary>
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
        /// <summary>Craft verbs run here (hand stations need a colonist; machines auto-work).</summary>
        public bool IsStation { get; }
        /// <summary>Auto-working powered machine (docs/plan/03 generic machine model).</summary>
        public bool IsMachine { get; }
        public float PowerKw { get; }
        public PowerPriority Priority { get; }
        public bool IsPowerPylon { get; }
        public bool IsGasPylon { get; }
        /// <summary>Items this extraction machine pulls from nearby deposit nodes.</summary>
        public IReadOnlyList<string> Extracts { get; }
        public IReadOnlyList<Ingredient> BuildCost { get; }
        public int BuildWorkTicks { get; }
        public bool Demolishable { get; }
        /// <summary>Tech node gating construction (empty = always available).</summary>
        public string TechNode { get; }

        public BuildingDef(string id, int width, int height, BuildingKind kind,
            bool walkable = false, bool interior = false, int beds = 0, int storageCapacity = 0,
            bool isStation = false, bool isMachine = false, float powerKw = 0f,
            PowerPriority priority = PowerPriority.Production,
            bool isPowerPylon = false, bool isGasPylon = false, IReadOnlyList<string> extracts = null,
            IReadOnlyList<Ingredient> buildCost = null, int buildWorkTicks = 0,
            bool demolishable = true, string techNode = "")
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
            IsMachine = isMachine;
            PowerKw = powerKw;
            Priority = priority;
            IsPowerPylon = isPowerPylon;
            IsGasPylon = isGasPylon;
            Extracts = extracts ?? Array.Empty<string>();
            BuildCost = buildCost ?? Array.Empty<Ingredient>();
            BuildWorkTicks = buildWorkTicks;
            Demolishable = demolishable;
            TechNode = techNode;
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
        public const string PowerPylonId = "power_pylon";
        public const string SolarPanelId = "solar_panel";
        public const string WindTurbineId = "wind_turbine";
        public const string BatteryId = "battery";
        public const string GasPylonId = "gas_pylon";
        public const string GasTankId = "gas_tank";
        public const string ElectrolyzerId = "electrolyzer";
        public const string AirChargingStationId = "air_charging_station";
        public const string MinerId = "miner";
        public const string IceMinerId = "ice_miner";
        public const string CrusherId = "crusher";
        public const string FurnaceId = "furnace";
        public const string RollMillId = "roll_mill";
        public const string PressId = "press";
        public const string AssemblerId = "assembler";
        public const string WaterPurifierId = "water_purifier";
        public const string GreenhouseId = "greenhouse";
        public const string ForageStationId = "forage_station";
        public const string BotStationId = "bot_station";
        public const string ChargingPostId = "charging_post";
        public const string ResearchBenchId = "research_bench";

        private const int QuickBuildTicks = 150;
        private const int NormalBuildTicks = 300;
        private const int BigBuildTicks = 450;
        private const int SmallStorageStacks = 24;

        // Machine power draws (docs/plan/03 数值锚点; moves into generated data at M3).
        private const float ElectrolyzerKw = 40f;
        private const float AirChargingKw = 5f;
        private const float MinerKw = 20f;
        private const float IceMinerKw = 15f;
        private const float CrusherKw = 15f;
        private const float FurnaceKw = 35f;
        private const float RollMillKw = 20f;
        private const float PressKw = 20f;
        private const float AssemblerKw = 25f;
        private const float PurifierKw = 10f;
        private const float GreenhouseKw = 8f;
        private const float ForageKw = 5f;
        private const float ChargingPostKw = 10f;

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

        // ---------------------------------------------------------------- M2 power

        public static readonly BuildingDef PowerPylon =
            new BuildingDef(PowerPylonId, 1, 1, BuildingKind.PowerPylon, isPowerPylon: true,
                buildCost: new[] { Need(ItemIds.IronLump, 1), Need(ItemIds.CopperLump, 1) },
                buildWorkTicks: QuickBuildTicks, techNode: "power_basics");

        public static readonly BuildingDef SolarPanel =
            new BuildingDef(SolarPanelId, 2, 2, BuildingKind.SolarPanel,
                buildCost: new[] { Need(ItemIds.CrudeGlass, 2), Need(ItemIds.CopperLump, 1), Need(ItemIds.IronLump, 1) },
                buildWorkTicks: NormalBuildTicks, techNode: "power_basics");

        public static readonly BuildingDef WindTurbine =
            new BuildingDef(WindTurbineId, 1, 1, BuildingKind.WindTurbine,
                buildCost: new[] { Need(ItemIds.IronLump, 2), Need(ItemIds.Fiber, 2) },
                buildWorkTicks: NormalBuildTicks, techNode: "power_basics");

        public static readonly BuildingDef Battery =
            new BuildingDef(BatteryId, 1, 1, BuildingKind.Battery,
                buildCost: new[] { Need(ItemIds.CopperLump, 2), Need(ItemIds.Salt, 2), Need(ItemIds.CrudeGlass, 1) },
                buildWorkTicks: NormalBuildTicks, techNode: "power_storage");

        // ---------------------------------------------------------------- M2 oxygen

        public static readonly BuildingDef GasPylon =
            new BuildingDef(GasPylonId, 1, 1, BuildingKind.GasPylon, isGasPylon: true,
                buildCost: new[] { Need(ItemIds.IronLump, 1) },
                buildWorkTicks: QuickBuildTicks, techNode: "oxygen_network");

        public static readonly BuildingDef GasTank =
            new BuildingDef(GasTankId, 2, 2, BuildingKind.GasTank,
                buildCost: new[] { Need(ItemIds.IronLump, 3) },
                buildWorkTicks: NormalBuildTicks, techNode: "oxygen_network");

        public static readonly BuildingDef Electrolyzer =
            new BuildingDef(ElectrolyzerId, 2, 2, BuildingKind.Electrolyzer,
                powerKw: ElectrolyzerKw, priority: PowerPriority.LifeSupport,
                buildCost: new[] { Need(ItemIds.CopperLump, 2), Need(ItemIds.IronLump, 2), Need(ItemIds.CrudeGlass, 1) },
                buildWorkTicks: BigBuildTicks, techNode: "oxygen_network");

        public static readonly BuildingDef AirChargingStation =
            new BuildingDef(AirChargingStationId, 1, 1, BuildingKind.AirChargingStation,
                powerKw: 5f, priority: PowerPriority.LifeSupport,
                buildCost: new[] { Need(ItemIds.IronLump, 2), Need(ItemIds.Fiber, 1) },
                buildWorkTicks: QuickBuildTicks, techNode: "oxygen_network");

        // ---------------------------------------------------------------- M2 machines

        public static readonly BuildingDef Miner =
            new BuildingDef(MinerId, 2, 2, BuildingKind.Miner, isMachine: true,
                powerKw: MinerKw, extracts: new[] { ItemIds.IronOre, ItemIds.CopperOre, ItemIds.QuartzSand, ItemIds.SaltOre, ItemIds.Carbon },
                buildCost: new[] { Need(ItemIds.IronLump, 3), Need(ItemIds.CopperLump, 1), Need(ItemIds.CrudeTool, 1) },
                buildWorkTicks: BigBuildTicks, techNode: "powered_extraction");

        public static readonly BuildingDef IceMiner =
            new BuildingDef(IceMinerId, 2, 2, BuildingKind.IceMiner, isMachine: true,
                powerKw: IceMinerKw, priority: PowerPriority.LifeSupport, extracts: new[] { ItemIds.Ice },
                buildCost: new[] { Need(ItemIds.IronLump, 3), Need(ItemIds.CrudeTool, 1) },
                buildWorkTicks: BigBuildTicks, techNode: "powered_extraction");

        public static readonly BuildingDef Crusher =
            new BuildingDef(CrusherId, 2, 1, BuildingKind.Crusher, isStation: true, isMachine: true,
                powerKw: CrusherKw,
                buildCost: new[] { Need(ItemIds.IronLump, 3), Need(ItemIds.CrudeTool, 1) },
                buildWorkTicks: NormalBuildTicks, techNode: "powered_processing");

        public static readonly BuildingDef Furnace =
            new BuildingDef(FurnaceId, 2, 2, BuildingKind.Furnace, isStation: true, isMachine: true,
                powerKw: FurnaceKw,
                buildCost: new[] { Need(ItemIds.IronLump, 2), Need(ItemIds.CarbonPowder, 2) },
                buildWorkTicks: NormalBuildTicks, techNode: "powered_processing");

        public static readonly BuildingDef RollMill =
            new BuildingDef(RollMillId, 2, 1, BuildingKind.RollMill, isStation: true, isMachine: true,
                powerKw: RollMillKw,
                buildCost: new[] { Need(ItemIds.IronLump, 3) },
                buildWorkTicks: NormalBuildTicks, techNode: "powered_processing");

        public static readonly BuildingDef Press =
            new BuildingDef(PressId, 2, 1, BuildingKind.Press, isStation: true, isMachine: true,
                powerKw: PressKw,
                buildCost: new[] { Need(ItemIds.IronLump, 3) },
                buildWorkTicks: NormalBuildTicks, techNode: "powered_processing");

        public static readonly BuildingDef Assembler =
            new BuildingDef(AssemblerId, 2, 2, BuildingKind.Assembler, isStation: true, isMachine: true,
                powerKw: AssemblerKw,
                buildCost: new[] { Need(ItemIds.IronLump, 2), Need(ItemIds.CopperLump, 2), Need(ItemIds.CrudeTool, 1) },
                buildWorkTicks: BigBuildTicks, techNode: "powered_assembly");

        public static readonly BuildingDef WaterPurifier =
            new BuildingDef(WaterPurifierId, 1, 2, BuildingKind.WaterPurifier, isStation: true, isMachine: true,
                powerKw: PurifierKw, priority: PowerPriority.LifeSupport,
                buildCost: new[] { Need(ItemIds.IronLump, 2), Need(ItemIds.CrudeGlass, 1) },
                buildWorkTicks: NormalBuildTicks, techNode: "life_support_1");

        public static readonly BuildingDef Greenhouse =
            new BuildingDef(GreenhouseId, 3, 2, BuildingKind.Greenhouse, isStation: true, isMachine: true,
                powerKw: 8f, priority: PowerPriority.LifeSupport,
                buildCost: new[] { Need(ItemIds.CrudeGlass, 3), Need(ItemIds.Fiber, 2), Need(ItemIds.IronLump, 1) },
                buildWorkTicks: BigBuildTicks, techNode: "life_support_1");

        public static readonly BuildingDef ForageStation =
            new BuildingDef(ForageStationId, 1, 1, BuildingKind.ForageStation, isMachine: true,
                powerKw: 5f, extracts: new[] { ItemIds.Biomass },
                buildCost: new[] { Need(ItemIds.Fiber, 3), Need(ItemIds.IronLump, 1) },
                buildWorkTicks: NormalBuildTicks, techNode: "powered_extraction");

        public static readonly BuildingDef BotStation =
            new BuildingDef(BotStationId, 2, 2, BuildingKind.BotStation,
                buildCost: new[] { Need(ItemIds.IronLump, 3), Need(ItemIds.CopperLump, 2), Need(ItemIds.CrudeTool, 1) },
                buildWorkTicks: BigBuildTicks, techNode: "hauler_bots");

        public static readonly BuildingDef ChargingPost =
            new BuildingDef(ChargingPostId, 1, 1, BuildingKind.ChargingPost,
                powerKw: ChargingPostKw, priority: PowerPriority.Logistics,
                buildCost: new[] { Need(ItemIds.CopperLump, 2), Need(ItemIds.IronLump, 1) },
                buildWorkTicks: QuickBuildTicks, techNode: "hauler_bots");

        public static readonly BuildingDef ResearchBench =
            new BuildingDef(ResearchBenchId, 2, 1, BuildingKind.ResearchBench, isStation: true,
                buildCost: new[] { Need(ItemIds.IronLump, 2), Need(ItemIds.Fiber, 2), Need(ItemIds.CrudeGlass, 1) },
                buildWorkTicks: NormalBuildTicks);

        private static readonly Dictionary<string, BuildingDef> ById = new Dictionary<string, BuildingDef>
        {
            { TestBlock.Id, TestBlock },
            { CrashPod.Id, CrashPod },
            { Workbench.Id, Workbench },
            { Campfire.Id, Campfire },
            { SleepPod.Id, SleepPod },
            { SmallStorage.Id, SmallStorage },
            { HandCrank.Id, HandCrank },
            { Road.Id, Road },
            { PowerPylon.Id, PowerPylon },
            { SolarPanel.Id, SolarPanel },
            { WindTurbine.Id, WindTurbine },
            { Battery.Id, Battery },
            { GasPylon.Id, GasPylon },
            { GasTank.Id, GasTank },
            { Electrolyzer.Id, Electrolyzer },
            { AirChargingStation.Id, AirChargingStation },
            { Miner.Id, Miner },
            { IceMiner.Id, IceMiner },
            { Crusher.Id, Crusher },
            { Furnace.Id, Furnace },
            { RollMill.Id, RollMill },
            { Press.Id, Press },
            { Assembler.Id, Assembler },
            { WaterPurifier.Id, WaterPurifier },
            { Greenhouse.Id, Greenhouse },
            { ForageStation.Id, ForageStation },
            { BotStation.Id, BotStation },
            { ChargingPost.Id, ChargingPost },
            { ResearchBench.Id, ResearchBench }
        };

        /// <summary>T0 hand-tech buildings, always available (M1 build menu).</summary>
        public static readonly string[] BuildableT0 =
        {
            CampfireId, WorkbenchId, SleepPodId, SmallStorageId, HandCrankId, RoadId
        };

        /// <summary>Everything the build menu can offer once tech unlocks it (M2).</summary>
        public static readonly string[] BuildableAll =
        {
            CampfireId, WorkbenchId, SleepPodId, SmallStorageId, HandCrankId, RoadId,
            ResearchBenchId,
            PowerPylonId, SolarPanelId, WindTurbineId, BatteryId,
            GasPylonId, GasTankId, ElectrolyzerId, AirChargingStationId,
            MinerId, IceMinerId, CrusherId, FurnaceId, RollMillId, PressId, AssemblerId,
            WaterPurifierId, GreenhouseId, ForageStationId, BotStationId, ChargingPostId
        };

        public static bool TryGet(string id, out BuildingDef def) => ById.TryGetValue(id, out def);
    }

    public enum PlacementError
    {
        None,
        UnknownDef,
        OutOfBounds,
        NotFlat,
        Occupied,
        Locked,
        NeedsDeposit
    }

    public sealed class BuildingState
    {
        public int Id;
        public string DefId;
        public int X;
        public int Y;
        /// <summary>Quarter turns, 0..3.</summary>
        public int Rotation;
        /// <summary>0-100; storms and machine work wear this down (docs/plan/02+03).</summary>
        public float Durability = Balance.NeedMax;
        /// <summary>Hand crank staffing toggle.</summary>
        public bool StaffedRequested;
        /// <summary>True on ticks when a colonist is actually cranking (network supply).</summary>
        public bool CrankActive;
        /// <summary>Machine on/off toggle (also gates power demand).</summary>
        public bool WantsPower = true;
        /// <summary>Station input/output buffer or storage contents.</summary>
        public Inventory Stock = new Inventory();
        /// <summary>Outbound reservations against Stock (this building as a haul source).</summary>
        public Inventory Reserved = new Inventory();
        /// <summary>Inbound in-flight deliveries (this station as a haul destination).</summary>
        public Inventory Inbound = new Inventory();
        /// <summary>Colonist ids currently occupying beds here.</summary>
        public List<int> SleepersIds = new List<int>();
        /// <summary>Machine work accumulator (recipe progress or extraction/electrolysis).</summary>
        public float ProcessAccum;
        /// <summary>Stored energy for battery buildings.</summary>
        public float BatteryKwh;

        public bool NeedsRepair => Durability < Balance.LowDurabilityThreshold;
    }

    /// <summary>
    /// Building placement/removal rules (docs/plan/03): footprints must be in bounds,
    /// on flat same-height ground, and not overlap. Extraction machines must cover a
    /// matching deposit. Cells track the occupying building id.
    /// </summary>
    public sealed class BuildingSystem
    {
        private const int RotationCount = 4;

        private readonly TerrainGrid _terrain;
        private readonly Dictionary<int, BuildingState> _byId = new Dictionary<int, BuildingState>();
        private readonly int[] _occupancy;
        private int _nextId = 1;

        /// <summary>Set by World so placement can validate deposits and notify networks.</summary>
        internal World Owner;

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

            if (def.Extracts.Count > 0 && def.Kind != BuildingKind.ForageStation && Owner != null &&
                FindDepositFor(def, x, y) == 0)
            {
                return PlacementError.NeedsDeposit;
            }
            return PlacementError.None;
        }

        /// <summary>Nearest matching deposit node id within extraction radius, or 0.</summary>
        public int FindDepositFor(BuildingDef def, int x, int y)
        {
            if (Owner == null)
            {
                return 0;
            }
            int best = 0;
            int bestDistance = int.MaxValue;
            foreach (var pair in Owner.Nodes.All)
            {
                var node = pair.Value;
                bool matches = false;
                for (int i = 0; i < def.Extracts.Count; i++)
                {
                    if (def.Extracts[i] == node.ItemId)
                    {
                        matches = true;
                        break;
                    }
                }
                if (!matches)
                {
                    continue;
                }
                int distance = Math.Max(Math.Abs(node.X - x), Math.Abs(node.Y - y));
                if (distance <= Balance.ExtractionMachineRadius + (def.Kind == BuildingKind.ForageStation ? Balance.GasPylonCoverRadius : 0) &&
                    distance < bestDistance)
                {
                    best = pair.Key;
                    bestDistance = distance;
                }
            }
            return best;
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
            Owner?.Networks.MarkDirty();
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
            Owner?.Networks.MarkDirty();
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
                    StaffedRequested = s.StaffedRequested,
                    WantsPower = s.WantsPower,
                    ProcessAccum = s.ProcessAccum,
                    BatteryKwh = s.BatteryKwh
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
            Owner?.Networks.MarkDirty();
        }
    }
}
