using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    /// <summary>
    /// Root simulation aggregate for one region (multi-region arrives at M4).
    /// Step() advances exactly one tick in the fixed order from docs/plan/08:
    /// commands → dispatch → colonists → life support → hazards → tutorial → defeat.
    /// </summary>
    public sealed class World
    {
        public ulong Seed { get; }
        public long Tick { get; private set; }
        public TerrainGrid Terrain { get; }
        public BuildingSystem Buildings { get; }
        public CommandQueue Commands { get; }
        public PileSystem Piles { get; } = new PileSystem();
        public NodeSystem Nodes { get; } = new NodeSystem();
        public BlueprintSystem Blueprints { get; }
        public CraftingSystem Crafting { get; } = new CraftingSystem();
        public TaskSystem Tasks { get; } = new TaskSystem();
        public ColonistSystem Colonists { get; } = new ColonistSystem();
        public LifeSupportSystem Life { get; } = new LifeSupportSystem();
        public StormSystem Storm { get; } = new StormSystem();
        public TutorialSystem Tutorial { get; } = new TutorialSystem();
        public AlertSystem Alerts { get; } = new AlertSystem();
        public StatsSystem Stats { get; } = new StatsSystem();
        public NetworkSystem Networks { get; } = new NetworkSystem();
        public MachineSystem Machines { get; } = new MachineSystem();
        public BotSystem Bots { get; } = new BotSystem();
        public JobSystem Jobs { get; } = new JobSystem();
        public MoraleSystem Morale { get; } = new MoraleSystem();
        public TechSystem Tech { get; } = new TechSystem();

        /// <summary>Wind supply factor (0.6–1.4), re-rolled hourly from the wind stream.</summary>
        public float WindFactor { get; private set; } = 1f;

        public Pathfinding.Context PathContext { get; }

        /// <summary>Events raised during the most recent tick; cleared at the start of each tick.</summary>
        public List<ISimEvent> Events { get; } = new List<ISimEvent>();

        /// <summary>Named deterministic random streams, persisted in saves (docs/plan/08).</summary>
        private readonly Dictionary<string, Rng> _streams = new Dictionary<string, Rng>();

        public int StartX { get; private set; }
        public int StartY { get; private set; }
        public int PodInteriorX { get; private set; }
        public int PodInteriorY { get; private set; }
        public int GraveX { get; private set; }
        public int GraveY { get; private set; }
        public bool Defeated { get; private set; }

        /// <summary>Creates a world with the starting colony (crash pod, crew, nodes).</summary>
        public World(ulong seed, int regionSize)
            : this(seed, TerrainGenerator.Generate(regionSize, seed), spawnColony: true)
        {
        }

        /// <summary>New games start in the morning, not at midnight (first night comes
        /// after ten daylight hours; docs/plan/02 first-act pacing).</summary>
        private const int StartHourOfDay = 8;

        internal World(ulong seed, TerrainGrid terrain, bool spawnColony)
        {
            Seed = seed;
            Terrain = terrain;
            Buildings = new BuildingSystem(terrain);
            Buildings.Owner = this;
            Blueprints = new BlueprintSystem(terrain, Buildings);
            Commands = new CommandQueue();
            PathContext = new Pathfinding.Context { Terrain = terrain, Buildings = Buildings };
            if (spawnColony)
            {
                Tick = StartHourOfDay * GameConstants.TicksPerHour;
                SpawnStartingColony();
            }
        }

        private void SpawnStartingColony()
        {
            int center = Terrain.Size / 2;
            BuildingDefs.TryGet(BuildingDefs.CrashPodId, out var podDef);
            int podX = center - podDef.Width / 2;
            int podY = center - podDef.Height / 2;
            int podId = Buildings.Place(BuildingDefs.CrashPodId, podX, podY, 0, out _);
            Buildings.TryGet(podId, out var pod);
            pod.Stock.Add(ItemIds.Ration, Balance.StartRations);
            pod.Stock.Add(ItemIds.Water, Balance.StartWater);
            pod.Stock.Add(ItemIds.AlgaeSeed, Balance.StartAlgaeSeeds);
            pod.Stock.Add(ItemIds.OxygenBottle, Balance.StartSpareBottles);

            StartX = center;
            StartY = center;
            PodInteriorX = podX + 1;
            PodInteriorY = podY + 1;
            GraveX = Math.Max(1, podX - 6);
            GraveY = Math.Max(1, podY - 6);

            Life.TankO2 = Balance.PodO2TankCapacity;

            Nodes.Generate(Terrain, Buildings, StartX, StartY, GetStream("resources"));

            for (int i = 0; i < Balance.StartColonists; i++)
            {
                Colonists.Spawn(podX - 1, podY + i);
            }

            Storm.ScheduleFirst(this);
        }

        public void Step()
        {
            Events.Clear();
            Commands.Drain(this);
            if (Tick % GameConstants.TicksPerHour == 0)
            {
                RollWind();
                Morale.TickHourly(this);
            }
            if (Tick % Balance.DispatchIntervalTicks == 0)
            {
                Bots.EnsureStationBots(this);
                Tasks.GenerateAndDispatch(this);
            }
            Life.BeginTick();
            ResetCrankFlags();
            Colonists.Tick(this);
            Bots.Tick(this);
            Machines.Tick(this);
            Networks.Tick(this);
            Life.EndTick(this);
            Storm.Tick(this);
            Tutorial.Tick(this);
            CheckDefeat();
            Tick++;
        }

        private void RollWind()
        {
            var stream = GetStream("wind");
            WindFactor = 1f - Balance.WindFluctuation + stream.NextFloat() * (2f * Balance.WindFluctuation);
        }

        private void ResetCrankFlags()
        {
            foreach (var building in Buildings.All.Values)
            {
                building.CrankActive = false;
            }
        }

        private void CheckDefeat()
        {
            if (!Defeated && Colonists.All.Count > 0 && Colonists.AliveCount == 0)
            {
                Defeated = true;
                Alerts.Raise(this, AlertIds.Defeat, AlertSeverity.Critical, PodInteriorX, PodInteriorY);
                Events.Add(new DefeatEvent { DaysSurvived = Day });
            }
        }

        public long Day => Tick / GameConstants.TicksPerDay;

        public int HourOfDay => (int)(Tick / GameConstants.TicksPerHour % GameConstants.HoursPerDay);

        public bool IsNight => HourOfDay < Balance.DayStartHour || HourOfDay >= Balance.NightStartHour;

        public Rng GetStream(string name)
        {
            if (!_streams.TryGetValue(name, out var rng))
            {
                rng = Rng.CreateStream(Seed, name);
                _streams.Add(name, rng);
            }
            return rng;
        }

        internal Dictionary<string, Rng> StreamsForSave() => _streams;

        internal void RestoreTick(long tick)
        {
            Tick = tick;
        }

        internal void RestoreMeta(SaveData data)
        {
            StartX = data.StartX;
            StartY = data.StartY;
            PodInteriorX = data.PodInteriorX;
            PodInteriorY = data.PodInteriorY;
            GraveX = data.GraveX;
            GraveY = data.GraveY;
            Defeated = data.Defeated;
            Life.TankO2 = data.O2Tank;
            Storm.State = (StormState)data.StormState;
            Storm.NextStartTick = data.StormNextStartTick;
            Storm.EndTick = data.StormEndTick;
            Tutorial.StepIndex = data.TutorialStep;
            Tutorial.Skipped = data.TutorialSkipped;
            Stats.Mined = data.StatsMined ?? new Dictionary<string, int>();
            Stats.Crafted = data.StatsCrafted ?? new Dictionary<string, int>();
            Stats.Built = data.StatsBuilt ?? new Dictionary<string, int>();
            Stats.Deaths = data.StatsDeaths ?? new Dictionary<string, int>();
            Stats.Burials = data.StatsBurials;
            _streams.Clear();
            if (data.RngStreams != null)
            {
                foreach (var s in data.RngStreams)
                {
                    _streams[s.Name] = Rng.FromState(s.State, s.Inc);
                }
            }

            Tech.RestoreUnlocked(data.TechUnlocked, data.ResearchTarget, data.ResearchPaid);
            if (data.JobQuotas != null)
            {
                foreach (var stack in data.JobQuotas)
                {
                    if (Enum.TryParse(stack.ItemId, out JobType job))
                    {
                        Jobs.Quotas[job] = stack.Count;
                    }
                }
            }
            if (data.JobMatrix != null && data.JobMatrix.Count == JobSystem.JobCount * JobSystem.TaskTypeCount)
            {
                int index = 0;
                for (int j = 0; j < JobSystem.JobCount; j++)
                {
                    for (int t = 0; t < JobSystem.TaskTypeCount; t++)
                    {
                        Jobs.Matrix[j, t] = data.JobMatrix[index];
                        index++;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- stock helpers

        /// <summary>Total item count across ground piles, building stocks and carried loads.</summary>
        public int CountItemEverywhere(string itemId)
        {
            int total = Piles.CountOf(itemId);
            foreach (var building in Buildings.All.Values)
            {
                total += building.Stock.Get(itemId);
            }
            foreach (var colonist in Colonists.All.Values)
            {
                if (colonist.CarryingItem == itemId)
                {
                    total += colonist.CarryingCount;
                }
            }
            return total;
        }

        /// <summary>Nearest cell holding available stock of the item (piles first, then buildings).</summary>
        public bool TryFindStock(string itemId, int fromX, int fromY, out int x, out int y)
        {
            x = 0;
            y = 0;
            int bestDistance = int.MaxValue;
            bool found = false;
            foreach (var pile in Piles.All.Values)
            {
                if (pile.ItemId != itemId || pile.Available <= 0)
                {
                    continue;
                }
                int d = Math.Abs(pile.X - fromX) + Math.Abs(pile.Y - fromY);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    x = pile.X;
                    y = pile.Y;
                    found = true;
                }
            }
            foreach (var building in Buildings.All.Values)
            {
                if (building.Stock.Get(itemId) - building.Reserved.Get(itemId) <= 0)
                {
                    continue;
                }
                int d = Math.Abs(building.X - fromX) + Math.Abs(building.Y - fromY);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    x = building.X;
                    y = building.Y;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>Consumes one unit of the item from a pile or building stock at the cell.</summary>
        public bool TryConsumeItemAt(string itemId, int x, int y)
        {
            foreach (var pile in Piles.All.Values)
            {
                if (pile.X == x && pile.Y == y && pile.ItemId == itemId && pile.Count > 0)
                {
                    Piles.Take(pile, 1);
                    return true;
                }
            }
            int buildingId = Buildings.GetBuildingAt(x, y);
            if (buildingId != 0 && Buildings.TryGet(buildingId, out var building))
            {
                return building.Stock.TryRemove(itemId, 1);
            }
            return false;
        }

        /// <summary>Consumes one unit from anywhere (bandage auto-use).</summary>
        public bool TryConsumeItemAnywhere(string itemId)
        {
            foreach (var pile in Piles.All.Values)
            {
                if (pile.ItemId == itemId && pile.Count > 0)
                {
                    Piles.Take(pile, 1);
                    return true;
                }
            }
            foreach (var building in Buildings.All.Values)
            {
                if (building.Stock.TryRemove(itemId, 1))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Deterministic fingerprint over the canonical save capture (docs/plan/08).</summary>
        public ulong ComputeStateHash()
        {
            string json = SaveSerializer.ToCanonicalJson(SaveSerializer.Capture(this));
            return Fnv1a64.HashString(json);
        }
    }
}
