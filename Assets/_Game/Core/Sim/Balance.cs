namespace Starsoil.Core
{
    /// <summary>
    /// M1 survival tunables (docs/plan/02 needs table, all converted to per-tick units;
    /// 1 game hour = 500 ticks). Values sit inside the plan's ±50% balance window;
    /// structural rules live in the systems, numbers live here.
    /// </summary>
    public static class Balance
    {
        public const float NeedMax = 100f;

        // Oxygen (docs/plan/02): indoor 6/h from the pod tank, suit 12/h from the bottle,
        // no air = 250/h need loss. Bottle holds 30 (≈2.5h outdoors).
        public const float IndoorO2PerTick = 6f / GameConstants.TicksPerHour;
        public const float SuitO2PerTick = 12f / GameConstants.TicksPerHour;
        public const float NoAirNeedLossPerTick = 250f / GameConstants.TicksPerHour;
        public const float BottleCapacity = 30f;
        public const float BottleRefillThreshold = 8f;
        public const float O2NeedRecoverPerTick = 300f / GameConstants.TicksPerHour;

        // Water / food: 100 per game day, refilled in one consume action.
        public const float WaterNeedLossPerTick = NeedMax / GameConstants.TicksPerDay;
        public const float FoodNeedLossPerTick = NeedMax / GameConstants.TicksPerDay;
        public const float DrinkAtThreshold = 30f;
        public const float EatAtThreshold = 30f;

        // Sleep: -100/16h awake; bed recovery +100/7h; ground sleep at half rate.
        public const float SleepLossPerTick = NeedMax / (16f * GameConstants.TicksPerHour);
        public const float SleepRecoverPerTick = NeedMax / (7f * GameConstants.TicksPerHour);
        public const float GroundSleepFactor = 0.5f;
        public const float SeekBedThreshold = 20f;
        public const int FaintDurationTicks = 2 * GameConstants.TicksPerHour;
        public const float WakeFromFaintSleep = 30f;

        // Temperature: night/outdoors -20/h (insulated suit halves, T1 item), indoor +50/h.
        public const float ColdLossPerTick = 20f / GameConstants.TicksPerHour;
        public const float WarmRecoverPerTick = 50f / GameConstants.TicksPerHour;
        public const float SeekWarmthThreshold = 30f;
        public const float StopWarmingThreshold = 70f;

        // Critical state: 1 game hour to rescue; a bandage buys +30 of the failing need.
        public const int CriticalDeathTicks = GameConstants.TicksPerHour;
        public const float BandageNeedRestore = 30f;

        // Day/night (docs/plan/02: Dustloam 12h day + 12h night).
        public const int DayStartHour = 6;
        public const int NightStartHour = 18;

        // Movement (docs/plan/02: 3.5 cells/s, roads ×1.4).
        public const float WalkCellsPerTick = 3.5f / GameConstants.TicksPerRealSecondAt1x;
        public const float RoadSpeedFactor = 1.4f;

        // Hand work rates (docs/plan/03: hand stations run at ×0.5, machines take over in M2).
        public const float HandcraftTimeFactor = 2f;
        public const int MineTicksPerUnit = 300;
        public const int GatherTicksPerUnit = 200;

        // Crash pod life support (M1 stand-in for the M2 oxygen network).
        public const float PodO2ProductionPerTick = 60f / GameConstants.TicksPerHour;
        public const float PodO2TankCapacity = 480f;
        public const float HandCrankKw = 1f;
        public const int PodBeds = 6;
        public const int PodStorageCapacity = 96;

        // Alert thresholds (docs/plan/02: banner when O2 reserve < 2h of consumption).
        public const int O2ReserveAlertHours = 2;
        public const float PerColonistO2EstimatePerHour = 12f;

        // Sandstorms (docs/plan/02 hazard table).
        public const int StormForecastLeadTicks = GameConstants.TicksPerDay;
        public const int StormMinDurationTicks = 6 * GameConstants.TicksPerHour;
        public const int StormMaxDurationTicks = 12 * GameConstants.TicksPerHour;
        public const int StormFirstEarliestDay = 2;
        public const int StormFirstLatestDay = 4;
        public const int StormGapMinDays = 3;
        public const int StormGapMaxDays = 6;
        public const float StormDurabilityLossPerHour = 2f;
        public const float MinDurability = 10f;
        public const float LowDurabilityThreshold = 20f;
        public const float LowDurabilitySpeedFactor = 0.5f;

        // Start-region generation guarantees (docs/plan/02).
        public const int GuaranteedDepositRadius = 60;
        public const int MinShrubsNearStart = 6;
        public const int TotalShrubs = 40;
        public const int DepositMinAmount = 300;
        public const int DepositMaxAmount = 600;
        public const int ShrubMinAmount = 10;
        public const int ShrubMaxAmount = 20;
        public const int ExtraDepositCount = 3;

        // Crash-pod starting inventory (docs/plan/02).
        public const int StartRations = 12;
        public const int StartWater = 8;
        public const int StartAlgaeSeeds = 20;
        public const int StartSpareBottles = 8;
        public const int StartColonists = 4;

        // Logistics.
        public const int DispatchIntervalTicks = 10;
        public const int PileMaxStack = 50;
        public const float DemolishRefundFactor = 0.5f;

        // Power network (docs/plan/03: pylon coverage 12, pylon-to-pylon link 24).
        public const int PowerPylonCoverRadius = 12;
        public const int PowerPylonLinkRange = 24;
        public const int PowerPriorityClassCount = 4;
        public const float SolarPanelKw = 15f;
        public const float WindTurbineKw = 10f;
        public const float WindFluctuation = 0.4f;
        public const float BatteryCapacityKwh = 300f;

        // Oxygen network (docs/plan/03: gas pylon radius 8; electrolyzer 120 O2/h).
        public const int GasPylonCoverRadius = 8;
        public const int GasPylonLinkRange = 16;
        public const float GasTankCapacity = 960f;
        public const float ElectrolyzerBufferCapacity = 240f;
        public const float ElectrolyzerO2PerTick = 120f / GameConstants.TicksPerHour;
        /// <summary>One water electrolyzes into this much breathable O2.</summary>
        public const float ElectrolyzerO2PerWater = 60f;
        /// <summary>Water buffer the logistics system keeps stocked in each electrolyzer.</summary>
        public const int ElectrolyzerWaterBuffer = 10;

        // Machines (docs/plan/03 generic machine model).
        public const float MachineLowDurabilityFactor = 0.5f;
        public const float MachineWearPerWorkHour = 0.5f;
        public const int ExtractionMachineRadius = 3;
        public const int ExtractorTicksPerUnit = 75;
        /// <summary>Extractors idle (and stop drawing power) once their buffer holds this much.</summary>
        public const int ExtractorOutputBufferCap = 40;
        public const float RepairGelRestore = 100f;

        // Hauler bots (docs/plan/02: carry 4 stacks; recharge at charging posts).
        public const int BotCarryCapacity = 40;
        public const float BotBatteryMax = 100f;
        public const float BotBatteryDrainPerHourActive = 25f;
        public const float BotChargePerHour = 200f;
        public const float BotSeekChargeAt = 20f;
        public const float BotSpeedFactor = 1.1f;

        // Morale (docs/plan/02 table).
        public const float MoraleStart = 60f;
        public const float MoraleFoodVariety2 = 5f;
        public const float MoraleFoodVariety3 = 10f;
        public const float MoraleDeathPenalty = -20f;
        public const float MoraleNightShiftPenalty = -5f;
        public const float MoraleStormPenalty = -10f;
        public const float MoraleEventDecayPerDay = 5f;
        public const float MoraleHighThreshold = 70f;
        public const float MoraleLowThreshold = 30f;
        public const float MoraleStrikeThreshold = 20f;
        public const float MoraleHighSpeedBonus = 1.1f;
        public const float MoraleLowSpeedMalus = 0.85f;

        // Research (docs/plan/04 data cores; M2 ships the T0-T2 tree subset).
        public const int ResearchTicksPerCore = 250;

        // Needs-driven eating/drinking consume whole items.
        public const float DrinkRestore = NeedMax;
        public const float EatRestore = NeedMax;
    }
}
