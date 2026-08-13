using System.Collections.Generic;

namespace Starsoil.Core
{
    public sealed class SavedStack
    {
        public string ItemId;
        public int Count;
    }

    public sealed class SavedBuilding
    {
        public int Id;
        public string DefId;
        public int X;
        public int Y;
        public int Rotation;
        public float Durability = Balance.NeedMax;
        public bool StaffedRequested;
        public bool WantsPower = true;
        public float ProcessAccum;
        public float BatteryKwh;
        public List<SavedStack> Stock;
        public string PadTargetBody;
        public string PadPayload;
        public int PadCrew;
        public bool PadActive;
        public List<SavedStack> PadCargo;
    }

    public sealed class SavedGasComponent
    {
        public int ComponentId;
        public float Stored;
    }

    public sealed class SavedBot
    {
        public int Id;
        public int HomeStationId;
        public int X;
        public int Y;
        public float Battery;
    }

    public sealed class SavedPile
    {
        public int Id;
        public string ItemId;
        public int Count;
        public int X;
        public int Y;
    }

    public sealed class SavedNode
    {
        public int Id;
        public string ItemId;
        public int Remaining;
        public int X;
        public int Y;
        public bool Designated;
        public int TicksPerUnit;
        public bool FactionLocked;
    }

    public sealed class SavedBlueprint
    {
        public int Id;
        public string DefId;
        public int X;
        public int Y;
        public int Rotation;
        public float BuildProgress;
        public List<SavedStack> Delivered;
    }

    public sealed class SavedColonist
    {
        public int Id;
        public int X;
        public int Y;
        public float O2;
        public float Water;
        public float Food;
        public float Sleep;
        public float Temp;
        public float BottleO2;
        public bool Alive;
        public int CriticalCause;
        public int CriticalTicksLeft;
        public int FaintTicksLeft;
        public int Job;
        public float Morale = Balance.MoraleStart;
        public float MoraleEventOffset;
        public bool OnStrike;
        public int FoodVarietyYesterday;
        public int NightWorkHours;
    }

    public sealed class SavedCraftOrder
    {
        public int Id;
        public int StationId;
        public string RecipeId;
        public int Remaining;
        public int MaintainTarget;
    }

    public sealed class SavedAlert
    {
        public string Id;
        public int Severity;
        public int X;
        public int Y;
    }

    public sealed class SavedCombatUnit
    {
        public int Id;
        public int Side;
        public int X;
        public int Y;
        public float Hp;
        public float MaxHp;
        public float DamagePerHit;
        public int Order;
        public int HoldX;
        public int HoldY;
        public int PatrolAx;
        public int PatrolAy;
        public int PatrolBx;
        public int PatrolBy;
        public bool Armored;
    }

    public sealed class SavedWave
    {
        public long Tick;
        public int Count;
    }

    public sealed class SavedRngStream
    {
        public string Name;
        public ulong State;
        public ulong Inc;
    }

    /// <summary>
    /// Save schema v1 (docs/plan/08): single-region colony. Transient data (paths, claimed
    /// tasks, in-progress work) is intentionally not saved — tasks regenerate from world
    /// needs on the next dispatch round; carried loads are normalized into ground piles
    /// at capture time. Schema changes bump GameConstants.SaveSchemaVersion and register
    /// a migration in SaveMigrations.
    /// </summary>
    public sealed class SaveData
    {
        public int SchemaVersion = GameConstants.SaveSchemaVersion;
        public ulong Seed;
        public long Tick;
        public int RegionSize;
        public byte[] TerrainHeights;
        public List<SavedBuilding> Buildings = new List<SavedBuilding>();
        public List<SavedPile> Piles = new List<SavedPile>();
        public List<SavedNode> Nodes = new List<SavedNode>();
        public List<SavedBlueprint> Blueprints = new List<SavedBlueprint>();
        public List<SavedColonist> Colonists = new List<SavedColonist>();
        public List<SavedCraftOrder> CraftOrders = new List<SavedCraftOrder>();
        public List<SavedAlert> Alerts = new List<SavedAlert>();
        public List<SavedRngStream> RngStreams = new List<SavedRngStream>();
        public List<SavedGasComponent> GasComponents = new List<SavedGasComponent>();
        public List<SavedBot> Bots = new List<SavedBot>();
        public List<SavedCombatUnit> CombatUnits = new List<SavedCombatUnit>();
        public List<SavedWave> PendingWaves = new List<SavedWave>();
        public int RallyX;
        public int RallyY;
        public int ShieldTicksRemaining;
        public int ActiveRaidStrength;
        public List<string> TechUnlocked = new List<string>();
        public string ResearchTarget = string.Empty;
        public List<SavedStack> ResearchPaid = new List<SavedStack>();
        public List<SavedStack> JobQuotas = new List<SavedStack>();
        public List<int> JobMatrix = new List<int>();

        public int StartX;
        public int StartY;
        public int PodInteriorX;
        public int PodInteriorY;
        public int GraveX;
        public int GraveY;
        public bool Defeated;
        public float O2Tank;
        public int StormState;
        public long StormNextStartTick;
        public long StormEndTick;
        public int TutorialStep;
        public bool TutorialSkipped;
        public Dictionary<string, int> StatsMined = new Dictionary<string, int>();
        public Dictionary<string, int> StatsCrafted = new Dictionary<string, int>();
        public Dictionary<string, int> StatsBuilt = new Dictionary<string, int>();
        public Dictionary<string, int> StatsDeaths = new Dictionary<string, int>();
        public int StatsBurials;
    }
}
