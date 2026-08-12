using System;

namespace Starsoil.Core
{
    /// <summary>
    /// M1 life support: the crash pod's built-in O2 tank (stand-in for the M2 oxygen
    /// network) and the hand-crank power ledger (stand-in for the M2 power network).
    /// Also owns the low-reserve alerts (docs/plan/02 anti-death-spiral guards).
    /// </summary>
    public sealed class LifeSupportSystem
    {
        public float TankO2;
        private int _crankingCount;

        public float PowerKw { get; private set; }

        public void BeginTick()
        {
            _crankingCount = 0;
        }

        public void RegisterCrank()
        {
            _crankingCount++;
        }

        public bool TryDrawTank(float amount)
        {
            if (TankO2 < amount)
            {
                return false;
            }
            TankO2 -= amount;
            return true;
        }

        public void RefillBottle(Colonist colonist)
        {
            float wanted = Balance.BottleCapacity - colonist.BottleO2;
            float draw = Math.Min(wanted, TankO2);
            if (draw <= 0f)
            {
                return;
            }
            TankO2 -= draw;
            colonist.BottleO2 += draw;
        }

        public void EndTick(World world)
        {
            bool hasPod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod) != null;
            if (hasPod)
            {
                TankO2 = Math.Min(Balance.PodO2TankCapacity, TankO2 + Balance.PodO2ProductionPerTick);
            }
            PowerKw = _crankingCount * Balance.HandCrankKw;

            UpdateReserveAlerts(world);
        }

        private void UpdateReserveAlerts(World world)
        {
            int alive = world.Colonists.AliveCount;
            if (alive == 0)
            {
                return;
            }

            float spareBottleO2 = world.CountItemEverywhere(ItemIds.OxygenBottle) * Balance.BottleCapacity;
            float reserve = TankO2 + spareBottleO2;
            float needed = alive * Balance.PerColonistO2EstimatePerHour * Balance.O2ReserveAlertHours;
            if (reserve < needed)
            {
                world.Alerts.Raise(world, AlertIds.OxygenLow, AlertSeverity.Critical,
                    world.PodInteriorX, world.PodInteriorY);
            }
            else
            {
                world.Alerts.Clear(world, AlertIds.OxygenLow);
            }

            int food = 0;
            foreach (string f in ItemIds.Foods)
            {
                food += world.CountItemEverywhere(f);
            }
            SetWarning(world, AlertIds.FoodLow, food < alive);
            SetWarning(world, AlertIds.WaterLow, world.CountItemEverywhere(ItemIds.Water) < alive);
        }

        private static void SetWarning(World world, string alertId, bool active)
        {
            if (active)
            {
                world.Alerts.Raise(world, alertId, AlertSeverity.Warning, world.PodInteriorX, world.PodInteriorY);
            }
            else
            {
                world.Alerts.Clear(world, alertId);
            }
        }
    }

    public enum StormState
    {
        Idle,
        Forecast,
        Active
    }

    /// <summary>
    /// Sandstorms (docs/plan/02 hazard table): forecast exactly one game day ahead,
    /// 6–12h duration, outdoor tasks suspended (TaskSystem.Dispatch + colonist interrupts)
    /// and exposed stations lose durability while the storm runs.
    /// </summary>
    public sealed class StormSystem
    {
        public StormState State = StormState.Idle;
        public long NextStartTick;
        public long EndTick;

        public bool Active => State == StormState.Active;

        public void ScheduleFirst(World world)
        {
            var stream = world.GetStream("hazards");
            int startDay = stream.NextInt(Balance.StormFirstEarliestDay, Balance.StormFirstLatestDay + 1);
            NextStartTick = (long)startDay * GameConstants.TicksPerDay +
                            stream.NextInt(0, GameConstants.TicksPerDay);
            State = StormState.Idle;
        }

        public void Tick(World world)
        {
            switch (State)
            {
                case StormState.Idle:
                    if (world.Tick >= NextStartTick - Balance.StormForecastLeadTicks)
                    {
                        State = StormState.Forecast;
                        world.Alerts.Raise(world, AlertIds.StormIncoming, AlertSeverity.Warning,
                            world.PodInteriorX, world.PodInteriorY);
                        world.Events.Add(new StormForecastEvent { StartTick = NextStartTick });
                    }
                    break;

                case StormState.Forecast:
                    if (world.Tick >= NextStartTick)
                    {
                        var stream = world.GetStream("hazards");
                        EndTick = world.Tick + stream.NextInt(Balance.StormMinDurationTicks, Balance.StormMaxDurationTicks + 1);
                        State = StormState.Active;
                        world.Alerts.Clear(world, AlertIds.StormIncoming);
                        world.Alerts.Raise(world, AlertIds.StormActive, AlertSeverity.Critical,
                            world.PodInteriorX, world.PodInteriorY);
                        world.Events.Add(new StormStartedEvent { EndTick = EndTick });
                    }
                    break;

                case StormState.Active:
                    ApplyDurabilityWear(world);
                    if (world.Tick >= EndTick)
                    {
                        State = StormState.Idle;
                        ScheduleNext(world);
                        world.Alerts.Clear(world, AlertIds.StormActive);
                        world.Events.Add(new StormEndedEvent());
                    }
                    break;
            }
        }

        private void ScheduleNext(World world)
        {
            var stream = world.GetStream("hazards");
            int gapDays = stream.NextInt(Balance.StormGapMinDays, Balance.StormGapMaxDays + 1);
            NextStartTick = world.Tick + (long)gapDays * GameConstants.TicksPerDay +
                            stream.NextInt(0, GameConstants.TicksPerDay);
        }

        private static void ApplyDurabilityWear(World world)
        {
            float lossPerTick = Balance.StormDurabilityLossPerHour / GameConstants.TicksPerHour;
            foreach (var building in world.Buildings.All.Values)
            {
                if (!BuildingDefs.TryGet(building.DefId, out var def) || def.Interior)
                {
                    continue;
                }
                if (def.IsStation || def.Kind == BuildingKind.HandCrank)
                {
                    building.Durability = Math.Max(Balance.MinDurability, building.Durability - lossPerTick);
                }
            }
        }
    }
}
