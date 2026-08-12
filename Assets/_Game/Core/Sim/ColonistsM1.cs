using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    public enum ColonistActivity
    {
        Idle,
        GoingToTask,
        WorkingTask,
        GoingToDrink,
        GoingToEat,
        GoingToBed,
        Sleeping,
        GroundSleeping,
        GoingToWarm,
        Warming,
        GoingToRefill,
        Fainted,
        Dead
    }

    public enum DeathCause
    {
        None,
        Suffocation,
        Dehydration,
        Starvation,
        Hypothermia
    }

    public enum HaulPhase
    {
        None,
        ToSource,
        ToDest
    }

    public sealed class Colonist
    {
        public int Id;
        public int X;
        public int Y;
        public float MoveProgress;
        /// <summary>Transient path (not saved; replanned after load).</summary>
        public List<(int x, int y)> Path;
        public int PathIndex;
        public int PathGoalX = -1;
        public int PathGoalY = -1;

        public float O2 = Balance.NeedMax;
        public float Water = Balance.NeedMax;
        public float Food = Balance.NeedMax;
        public float Sleep = Balance.NeedMax;
        public float Temp = Balance.NeedMax;
        public float BottleO2 = Balance.BottleCapacity;

        public string CarryingItem;
        public int CarryingCount;

        public ColonistActivity Activity = ColonistActivity.Idle;
        public int TaskId;
        public HaulPhase Phase = HaulPhase.None;
        public float WorkAccum;
        public int BedBuildingId;
        public int StockTargetX = -1;
        public int StockTargetY = -1;
        /// <summary>Charging station chosen for the current refill trip (0 = crash pod).</summary>
        public int RefillBuildingId;
        /// <summary>Per-need retry cooldowns; a failed water search must not block eating.</summary>
        public int WaterSearchCooldown;
        public int FoodSearchCooldown;

        public DeathCause CriticalCause = DeathCause.None;
        public int CriticalTicksLeft;
        public int FaintTicksLeft;
        public bool Alive = true;

        public void AssignTask(WorkTask task)
        {
            TaskId = task.Id;
            Activity = ColonistActivity.GoingToTask;
            Phase = task.Type == TaskType.HaulToBlueprint || task.Type == TaskType.HaulToStation ||
                    task.Type == TaskType.HaulToStorage || task.Type == TaskType.Bury
                ? HaulPhase.ToSource
                : HaulPhase.None;
            WorkAccum = 0f;
            ClearPath();
        }

        public void ClearPath()
        {
            Path = null;
            PathIndex = 0;
            PathGoalX = -1;
            PathGoalY = -1;
            MoveProgress = 0f;
        }
    }

    /// <summary>
    /// Colonist needs, survival AI and movement (docs/plan/02 needs table, docs/plan/03
    /// dispatch contract). Colonists are ticked in ascending id order for determinism.
    /// </summary>
    public sealed class ColonistSystem
    {
        private const float DiagonalDistance = 1.41421356f;
        private const int StockSearchCooldownTicks = 200;
        private const float GroundSleepWakeAt = 60f;

        private readonly Dictionary<int, Colonist> _byId = new Dictionary<int, Colonist>();
        private int _nextId = 1;

        public IReadOnlyDictionary<int, Colonist> All => _byId;

        public int AliveCount
        {
            get
            {
                int count = 0;
                foreach (var colonist in _byId.Values)
                {
                    if (colonist.Alive)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public List<Colonist> AllSorted()
        {
            var keys = new List<int>(_byId.Keys);
            keys.Sort();
            var result = new List<Colonist>(keys.Count);
            foreach (int key in keys)
            {
                result.Add(_byId[key]);
            }
            return result;
        }

        public Colonist Spawn(int x, int y)
        {
            var colonist = new Colonist { Id = _nextId, X = x, Y = y };
            _nextId++;
            _byId.Add(colonist.Id, colonist);
            return colonist;
        }

        public void Tick(World world)
        {
            foreach (var colonist in AllSorted())
            {
                if (!colonist.Alive)
                {
                    continue;
                }
                TickNeeds(world, colonist);
                if (!colonist.Alive)
                {
                    continue;
                }
                if (colonist.Activity == ColonistActivity.Fainted)
                {
                    TickFaint(colonist);
                    continue;
                }
                TickInterrupts(world, colonist);
                TickActivity(world, colonist);
            }
        }

        // ---------------------------------------------------------------- needs

        private void TickNeeds(World world, Colonist colonist)
        {
            bool indoor = world.Buildings.IsCellInterior(colonist.X, colonist.Y);
            bool sleeping = colonist.Activity == ColonistActivity.Sleeping;
            bool groundSleeping = colonist.Activity == ColonistActivity.GroundSleeping;

            // Oxygen: connected O2 network first, crash-pod tank as standby (M2-T2).
            if (indoor)
            {
                int buildingId = world.Buildings.GetBuildingAt(colonist.X, colonist.Y);
                if (world.Networks.TryDrawO2ForIndoor(world, buildingId, Balance.IndoorO2PerTick))
                {
                    colonist.O2 = Math.Min(Balance.NeedMax, colonist.O2 + Balance.O2NeedRecoverPerTick);
                }
                else
                {
                    colonist.O2 -= Balance.NoAirNeedLossPerTick;
                }
            }
            else if (colonist.BottleO2 > 0f)
            {
                colonist.BottleO2 = Math.Max(0f, colonist.BottleO2 - Balance.SuitO2PerTick);
                colonist.O2 = Math.Min(Balance.NeedMax, colonist.O2 + Balance.O2NeedRecoverPerTick);
            }
            else
            {
                colonist.O2 -= Balance.NoAirNeedLossPerTick;
            }

            // Water / food.
            colonist.Water -= Balance.WaterNeedLossPerTick;
            colonist.Food -= Balance.FoodNeedLossPerTick;

            // Sleep.
            if (sleeping)
            {
                colonist.Sleep = Math.Min(Balance.NeedMax, colonist.Sleep + Balance.SleepRecoverPerTick);
            }
            else if (groundSleeping)
            {
                colonist.Sleep = Math.Min(Balance.NeedMax,
                    colonist.Sleep + Balance.SleepRecoverPerTick * Balance.GroundSleepFactor);
            }
            else
            {
                colonist.Sleep -= Balance.SleepLossPerTick;
            }

            // Temperature.
            if (indoor)
            {
                colonist.Temp = Math.Min(Balance.NeedMax, colonist.Temp + Balance.WarmRecoverPerTick);
            }
            else if (world.IsNight)
            {
                colonist.Temp -= Balance.ColdLossPerTick;
            }

            TickCritical(world, colonist);

            // Fainting from exhaustion (docs/plan/02: not lethal).
            if (colonist.Sleep <= 0f && colonist.Activity != ColonistActivity.Sleeping &&
                colonist.Activity != ColonistActivity.GroundSleeping && colonist.Activity != ColonistActivity.Fainted)
            {
                AbandonCurrent(world, colonist);
                colonist.Activity = ColonistActivity.Fainted;
                colonist.FaintTicksLeft = Balance.FaintDurationTicks;
                world.Events.Add(new ColonistFaintedEvent { ColonistId = colonist.Id });
            }
        }

        private void TickCritical(World world, Colonist colonist)
        {
            DeathCause cause = DeathCause.None;
            if (colonist.O2 <= 0f)
            {
                cause = DeathCause.Suffocation;
            }
            else if (colonist.Water <= 0f)
            {
                cause = DeathCause.Dehydration;
            }
            else if (colonist.Food <= 0f)
            {
                cause = DeathCause.Starvation;
            }
            else if (colonist.Temp <= 0f)
            {
                cause = DeathCause.Hypothermia;
            }

            if (cause == DeathCause.None)
            {
                if (colonist.CriticalCause != DeathCause.None)
                {
                    colonist.CriticalCause = DeathCause.None;
                    world.Alerts.Clear(world, AlertIds.ColonistCritical + colonist.Id);
                }
                return;
            }

            if (colonist.CriticalCause == DeathCause.None)
            {
                // A stocked bandage buys rescue time (docs/plan/02).
                if (world.TryConsumeItemAnywhere(ItemIds.Bandage))
                {
                    RestoreNeed(colonist, cause, Balance.BandageNeedRestore);
                    return;
                }
                colonist.CriticalCause = cause;
                colonist.CriticalTicksLeft = Balance.CriticalDeathTicks;
                world.Alerts.Raise(world, AlertIds.ColonistCritical + colonist.Id, AlertSeverity.Critical,
                    colonist.X, colonist.Y);
                return;
            }

            colonist.CriticalTicksLeft--;
            if (colonist.CriticalTicksLeft <= 0)
            {
                Die(world, colonist, cause);
            }
        }

        private static void RestoreNeed(Colonist colonist, DeathCause cause, float amount)
        {
            switch (cause)
            {
                case DeathCause.Suffocation: colonist.O2 = Math.Max(colonist.O2, amount); break;
                case DeathCause.Dehydration: colonist.Water = Math.Max(colonist.Water, amount); break;
                case DeathCause.Starvation: colonist.Food = Math.Max(colonist.Food, amount); break;
                case DeathCause.Hypothermia: colonist.Temp = Math.Max(colonist.Temp, amount); break;
            }
        }

        private void Die(World world, Colonist colonist, DeathCause cause)
        {
            AbandonCurrent(world, colonist);
            colonist.Alive = false;
            colonist.Activity = ColonistActivity.Dead;
            colonist.CriticalCause = cause;
            if (colonist.CarryingCount > 0)
            {
                world.Piles.Drop(colonist.CarryingItem, colonist.CarryingCount, colonist.X, colonist.Y);
                colonist.CarryingItem = null;
                colonist.CarryingCount = 0;
            }
            world.Piles.Drop(ItemIds.Remains, 1, colonist.X, colonist.Y);
            world.Stats.CountDeath(cause);
            world.Alerts.Clear(world, AlertIds.ColonistCritical + colonist.Id);
            world.Events.Add(new ColonistDiedEvent { ColonistId = colonist.Id, Cause = cause });
        }

        private void TickFaint(Colonist colonist)
        {
            colonist.FaintTicksLeft--;
            if (colonist.FaintTicksLeft <= 0)
            {
                colonist.Sleep = Balance.WakeFromFaintSleep;
                colonist.Activity = ColonistActivity.Idle;
            }
        }

        // ---------------------------------------------------------------- interrupts

        /// <summary>
        /// Urgency ladder: a colonist already answering a need can only be preempted by a
        /// strictly more urgent one. Without this, competing interrupts flip the activity
        /// (and clear the path) every tick and the colonist freezes in place.
        /// </summary>
        private const int UrgencyNone = 9;

        private static int Urgency(ColonistActivity activity)
        {
            switch (activity)
            {
                case ColonistActivity.GoingToRefill: return 2;
                case ColonistActivity.GoingToWarm:
                case ColonistActivity.Warming: return 3;
                case ColonistActivity.GoingToDrink: return 4;
                case ColonistActivity.GoingToEat: return 5;
                case ColonistActivity.GoingToBed:
                case ColonistActivity.Sleeping:
                case ColonistActivity.GroundSleeping: return 6;
                default: return UrgencyNone;
            }
        }

        private void TickInterrupts(World world, Colonist colonist)
        {
            bool indoor = world.Buildings.IsCellInterior(colonist.X, colonist.Y);
            int current = Urgency(colonist.Activity);

            // Storm shelters everyone working outdoors (docs/plan/02 sandstorm); need trips continue.
            if (world.Storm.Active && colonist.TaskId != 0)
            {
                AbandonCurrent(world, colonist);
                colonist.Activity = ColonistActivity.GoingToWarm;
                colonist.ClearPath();
                return;
            }

            // 2: empty-ish bottle outdoors → refill at a charging station or the pod.
            if (2 < current && !indoor && colonist.BottleO2 < Balance.BottleRefillThreshold)
            {
                var station = world.Networks.FindChargingStation(world);
                bool podHasAir = world.Life.TankO2 >= Balance.BottleCapacity;
                if (station != null || podHasAir)
                {
                    AbandonCurrent(world, colonist);
                    colonist.Activity = ColonistActivity.GoingToRefill;
                    colonist.RefillBuildingId = station?.Id ?? 0;
                    colonist.ClearPath();
                    return;
                }
            }

            // 3: freezing → warm up indoors.
            if (3 < current && colonist.Temp < Balance.SeekWarmthThreshold && !indoor)
            {
                AbandonCurrent(world, colonist);
                colonist.Activity = ColonistActivity.GoingToWarm;
                colonist.ClearPath();
                return;
            }

            if (colonist.WaterSearchCooldown > 0)
            {
                colonist.WaterSearchCooldown--;
            }
            if (colonist.FoodSearchCooldown > 0)
            {
                colonist.FoodSearchCooldown--;
            }

            // 4: thirst.
            if (4 < current && colonist.Water < Balance.DrinkAtThreshold && colonist.WaterSearchCooldown == 0)
            {
                if (world.TryFindStock(ItemIds.Water, colonist.X, colonist.Y, out int sx, out int sy))
                {
                    AbandonCurrent(world, colonist);
                    colonist.Activity = ColonistActivity.GoingToDrink;
                    colonist.StockTargetX = sx;
                    colonist.StockTargetY = sy;
                    colonist.ClearPath();
                    return;
                }
                colonist.WaterSearchCooldown = StockSearchCooldownTicks;
            }

            // 5: hunger.
            if (5 < current && colonist.Food < Balance.EatAtThreshold && colonist.FoodSearchCooldown == 0)
            {
                foreach (string food in ItemIds.Foods)
                {
                    if (world.TryFindStock(food, colonist.X, colonist.Y, out int sx, out int sy))
                    {
                        AbandonCurrent(world, colonist);
                        colonist.Activity = ColonistActivity.GoingToEat;
                        colonist.StockTargetX = sx;
                        colonist.StockTargetY = sy;
                        colonist.ClearPath();
                        return;
                    }
                }
                colonist.FoodSearchCooldown = StockSearchCooldownTicks;
            }

            // 6: sleep.
            if (6 < current && colonist.Sleep < Balance.SeekBedThreshold)
            {
                var bed = FindFreeBed(world);
                if (bed != null)
                {
                    AbandonCurrent(world, colonist);
                    bed.SleepersIds.Add(colonist.Id);
                    colonist.BedBuildingId = bed.Id;
                    colonist.Activity = ColonistActivity.GoingToBed;
                    colonist.ClearPath();
                }
                else if (colonist.Sleep < Balance.SeekBedThreshold / 4f)
                {
                    AbandonCurrent(world, colonist);
                    colonist.Activity = ColonistActivity.GroundSleeping;
                }
            }
        }

        private static BuildingState FindFreeBed(World world)
        {
            BuildingState best = null;
            foreach (var building in world.Buildings.All.Values)
            {
                if (!BuildingDefs.TryGet(building.DefId, out var def) || def.Beds <= 0)
                {
                    continue;
                }
                if (building.SleepersIds.Count < def.Beds && (best == null || building.Id < best.Id))
                {
                    best = building;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- activity

        private void TickActivity(World world, Colonist colonist)
        {
            switch (colonist.Activity)
            {
                case ColonistActivity.Idle:
                    break;

                case ColonistActivity.GoingToRefill:
                    TickGoingToRefill(world, colonist);
                    break;

                case ColonistActivity.GoingToWarm:
                    if (MoveToward(world, colonist, world.PodInteriorX, world.PodInteriorY))
                    {
                        colonist.Activity = ColonistActivity.Warming;
                    }
                    break;

                case ColonistActivity.Warming:
                    world.Life.RefillBottle(colonist);
                    if (colonist.Temp >= Balance.StopWarmingThreshold && !world.Storm.Active)
                    {
                        colonist.Activity = ColonistActivity.Idle;
                    }
                    break;

                case ColonistActivity.GoingToDrink:
                    TickConsumeTrip(world, colonist, ItemIds.Water, isFood: false);
                    break;

                case ColonistActivity.GoingToEat:
                    TickConsumeTrip(world, colonist, null, isFood: true);
                    break;

                case ColonistActivity.GoingToBed:
                    TickGoingToBed(world, colonist);
                    break;

                case ColonistActivity.Sleeping:
                    if (colonist.Sleep >= Balance.NeedMax)
                    {
                        ReleaseBed(world, colonist);
                        colonist.Activity = ColonistActivity.Idle;
                    }
                    break;

                case ColonistActivity.GroundSleeping:
                    if (colonist.Sleep >= GroundSleepWakeAt)
                    {
                        colonist.Activity = ColonistActivity.Idle;
                    }
                    break;

                case ColonistActivity.GoingToTask:
                case ColonistActivity.WorkingTask:
                    TickTask(world, colonist);
                    break;
            }
        }

        private void TickConsumeTrip(World world, Colonist colonist, string itemId, bool isFood)
        {
            if (colonist.StockTargetX < 0)
            {
                colonist.Activity = ColonistActivity.Idle;
                return;
            }
            if (!MoveToward(world, colonist, colonist.StockTargetX, colonist.StockTargetY))
            {
                return;
            }
            bool consumed;
            if (isFood)
            {
                consumed = false;
                foreach (string food in ItemIds.Foods)
                {
                    if (world.TryConsumeItemAt(food, colonist.StockTargetX, colonist.StockTargetY))
                    {
                        consumed = true;
                        break;
                    }
                }
                if (consumed)
                {
                    colonist.Food = Balance.EatRestore;
                }
            }
            else
            {
                consumed = world.TryConsumeItemAt(itemId, colonist.StockTargetX, colonist.StockTargetY);
                if (consumed)
                {
                    colonist.Water = Balance.DrinkRestore;
                }
            }
            if (!consumed)
            {
                colonist.WaterSearchCooldown = 0;
                colonist.FoodSearchCooldown = 0;
            }
            colonist.StockTargetX = -1;
            colonist.StockTargetY = -1;
            colonist.Activity = ColonistActivity.Idle;
        }

        private void TickGoingToRefill(World world, Colonist colonist)
        {
            // Charging station route (M2): draw the bottle from the station's O2 network.
            if (colonist.RefillBuildingId != 0)
            {
                if (!world.Buildings.TryGet(colonist.RefillBuildingId, out var station))
                {
                    colonist.RefillBuildingId = 0;
                    return;
                }
                if (!MoveToward(world, colonist, station.X, station.Y))
                {
                    return;
                }
                float wanted = Balance.BottleCapacity - colonist.BottleO2;
                if (wanted > 0f && world.Networks.TryDrawFromStationNetwork(world, station, wanted))
                {
                    colonist.BottleO2 = Balance.BottleCapacity;
                }
                else
                {
                    world.Life.RefillBottle(colonist);
                }
                colonist.RefillBuildingId = 0;
                colonist.Activity = ColonistActivity.Idle;
                return;
            }

            // Pod route (M1 behavior): refill and warm up in one trip.
            if (MoveToward(world, colonist, world.PodInteriorX, world.PodInteriorY))
            {
                world.Life.RefillBottle(colonist);
                colonist.Activity = colonist.Temp < Balance.StopWarmingThreshold
                    ? ColonistActivity.Warming
                    : ColonistActivity.Idle;
            }
        }

        private void TickGoingToBed(World world, Colonist colonist)
        {
            if (!world.Buildings.TryGet(colonist.BedBuildingId, out var bed))
            {
                colonist.BedBuildingId = 0;
                colonist.Activity = ColonistActivity.Idle;
                return;
            }
            if (MoveToward(world, colonist, bed.X, bed.Y))
            {
                colonist.Activity = ColonistActivity.Sleeping;
            }
        }

        private void ReleaseBed(World world, Colonist colonist)
        {
            if (colonist.BedBuildingId != 0 && world.Buildings.TryGet(colonist.BedBuildingId, out var bed))
            {
                bed.SleepersIds.Remove(colonist.Id);
            }
            colonist.BedBuildingId = 0;
        }

        // ---------------------------------------------------------------- tasks

        private void TickTask(World world, Colonist colonist)
        {
            if (!world.Tasks.TryGet(colonist.TaskId, out var task))
            {
                colonist.TaskId = 0;
                colonist.Activity = ColonistActivity.Idle;
                return;
            }

            switch (task.Type)
            {
                case TaskType.Mine:
                    TickMine(world, colonist, task);
                    break;
                case TaskType.Build:
                    TickBuild(world, colonist, task);
                    break;
                case TaskType.Craft:
                    TickCraft(world, colonist, task);
                    break;
                case TaskType.Crank:
                    TickCrank(world, colonist, task);
                    break;
                default:
                    TickHaul(world, colonist, task);
                    break;
            }
        }

        private void TickMine(World world, Colonist colonist, WorkTask task)
        {
            if (!world.Nodes.TryGet(task.NodeId, out var node) || !node.Designated)
            {
                CompleteTask(world, colonist, task);
                return;
            }
            if (!MoveToward(world, colonist, node.X, node.Y))
            {
                return;
            }
            colonist.Activity = ColonistActivity.WorkingTask;
            colonist.WorkAccum += 1f;
            if (colonist.WorkAccum < node.TicksPerUnit)
            {
                return;
            }
            colonist.WorkAccum = 0f;
            world.Piles.Drop(node.ItemId, 1, node.X, node.Y);
            world.Stats.CountMined(node.ItemId, 1);
            if (!world.Nodes.ExtractUnit(node))
            {
                world.Events.Add(new NodeDepletedEvent { NodeId = node.Id, X = node.X, Y = node.Y });
            }
            // Yield after every unit: mining is open-ended, so the worker returns to the
            // pool and higher-priority tasks (hauls, crafts, builds) get a chance. The
            // dispatcher recreates the mine task while the node stays designated.
            CompleteTask(world, colonist, task);
        }

        private void TickBuild(World world, Colonist colonist, WorkTask task)
        {
            if (!world.Blueprints.TryGet(task.BlueprintId, out var bp))
            {
                CompleteTask(world, colonist, task);
                return;
            }
            if (!MoveToward(world, colonist, bp.X, bp.Y))
            {
                return;
            }
            colonist.Activity = ColonistActivity.WorkingTask;
            if (world.Blueprints.ApplyBuildWork(bp, 1f, world) != 0)
            {
                CompleteTask(world, colonist, task);
            }
        }

        private void TickCraft(World world, Colonist colonist, WorkTask task)
        {
            if (!world.Buildings.TryGet(task.StationId, out var station) ||
                !world.Crafting.TryGetRecipe(FindOrderRecipe(world, station, task.OrderId), out var recipe) ||
                !world.Crafting.InputsReady(station, recipe))
            {
                CompleteTask(world, colonist, task);
                return;
            }
            if (!MoveToward(world, colonist, station.X, station.Y))
            {
                return;
            }
            colonist.Activity = ColonistActivity.WorkingTask;
            colonist.WorkAccum += 1f;
            if (colonist.WorkAccum >= CraftingSystem.EffectiveWorkTicks(recipe, station))
            {
                var order = FindOrder(world, station, task.OrderId);
                if (order != null)
                {
                    world.Crafting.CompleteCraft(world, station, order, recipe);
                }
                CompleteTask(world, colonist, task);
            }
        }

        private static string FindOrderRecipe(World world, BuildingState station, int orderId)
        {
            var order = FindOrder(world, station, orderId);
            return order?.RecipeId ?? string.Empty;
        }

        private static CraftOrder FindOrder(World world, BuildingState station, int orderId)
        {
            if (!world.Crafting.OrdersByStation.TryGetValue(station.Id, out var list))
            {
                return null;
            }
            foreach (var order in list)
            {
                if (order.Id == orderId)
                {
                    return order;
                }
            }
            return null;
        }

        private void TickCrank(World world, Colonist colonist, WorkTask task)
        {
            if (!world.Buildings.TryGet(task.BuildingId, out var crank) || !crank.StaffedRequested)
            {
                CompleteTask(world, colonist, task);
                return;
            }
            if (!MoveToward(world, colonist, crank.X, crank.Y))
            {
                return;
            }
            colonist.Activity = ColonistActivity.WorkingTask;
            crank.CrankActive = true;
            world.Life.RegisterCrank();
        }

        private void TickHaul(World world, Colonist colonist, WorkTask task)
        {
            if (colonist.Phase == HaulPhase.ToSource)
            {
                if (!SourceStillValid(world, task))
                {
                    world.Tasks.Remove(world, task, releaseReservations: true);
                    colonist.TaskId = 0;
                    colonist.Activity = ColonistActivity.Idle;
                    return;
                }
                GetSourceCell(world, task, out int sx, out int sy);
                if (!MoveToward(world, colonist, sx, sy))
                {
                    return;
                }
                PickUp(world, colonist, task);
                return;
            }

            // ToDest.
            if (!DestinationStillValid(world, task))
            {
                if (colonist.CarryingCount > 0)
                {
                    world.Piles.Drop(colonist.CarryingItem, colonist.CarryingCount, colonist.X, colonist.Y);
                    colonist.CarryingItem = null;
                    colonist.CarryingCount = 0;
                }
                world.Tasks.Remove(world, task, releaseReservations: false);
                colonist.TaskId = 0;
                colonist.Activity = ColonistActivity.Idle;
                return;
            }
            if (!MoveToward(world, colonist, task.TargetX, task.TargetY))
            {
                return;
            }
            Deliver(world, colonist, task);
        }

        private static bool SourceStillValid(World world, WorkTask task)
        {
            if (task.SourcePileId != 0)
            {
                return world.Piles.TryGet(task.SourcePileId, out var pile) && pile.Count > 0;
            }
            return world.Buildings.TryGet(task.SourceBuildingId, out var building) &&
                   building.Stock.Get(task.ItemId) > 0;
        }

        private static void GetSourceCell(World world, WorkTask task, out int x, out int y)
        {
            if (task.SourcePileId != 0 && world.Piles.TryGet(task.SourcePileId, out var pile))
            {
                x = pile.X;
                y = pile.Y;
                return;
            }
            world.Buildings.TryGet(task.SourceBuildingId, out var building);
            x = building.X;
            y = building.Y;
        }

        private bool DestinationStillValid(World world, WorkTask task)
        {
            switch (task.Type)
            {
                case TaskType.HaulToBlueprint:
                    return world.Blueprints.TryGet(task.BlueprintId, out _);
                case TaskType.HaulToStation:
                    return world.Buildings.TryGet(task.StationId, out _);
                case TaskType.HaulToStorage:
                    return world.Buildings.TryGet(task.BuildingId, out _);
                case TaskType.Bury:
                    return true;
                default:
                    return false;
            }
        }

        private void PickUp(World world, Colonist colonist, WorkTask task)
        {
            int taken = 0;
            if (task.SourcePileId != 0 && world.Piles.TryGet(task.SourcePileId, out var pile))
            {
                taken = Math.Min(task.Count, pile.Count);
                pile.Reserved = Math.Max(0, pile.Reserved - task.Count);
                world.Piles.Take(pile, taken);
            }
            else if (world.Buildings.TryGet(task.SourceBuildingId, out var building))
            {
                taken = Math.Min(task.Count, building.Stock.Get(task.ItemId));
                building.Stock.TryRemove(task.ItemId, taken);
                building.Reserved.Add(task.ItemId, -Math.Min(task.Count, building.Reserved.Get(task.ItemId)));
            }
            if (taken <= 0)
            {
                world.Tasks.Remove(world, task, releaseReservations: true);
                colonist.TaskId = 0;
                colonist.Activity = ColonistActivity.Idle;
                return;
            }
            colonist.CarryingItem = task.ItemId;
            colonist.CarryingCount = taken;
            colonist.Phase = HaulPhase.ToDest;
            colonist.ClearPath();
        }

        private void Deliver(World world, Colonist colonist, WorkTask task)
        {
            int count = colonist.CarryingCount;
            switch (task.Type)
            {
                case TaskType.HaulToBlueprint:
                    if (world.Blueprints.TryGet(task.BlueprintId, out var bp))
                    {
                        world.Blueprints.Deliver(bp, task.ItemId, count);
                    }
                    break;
                case TaskType.HaulToStation:
                    if (world.Buildings.TryGet(task.StationId, out var station))
                    {
                        station.Stock.Add(task.ItemId, count);
                        station.Inbound.Add(task.ItemId, -Math.Min(count, station.Inbound.Get(task.ItemId)));
                    }
                    break;
                case TaskType.HaulToStorage:
                    if (world.Buildings.TryGet(task.BuildingId, out var storage))
                    {
                        storage.Stock.Add(task.ItemId, count);
                    }
                    break;
                case TaskType.Bury:
                    world.Stats.CountBurial();
                    world.Events.Add(new ColonistBuriedEvent { X = task.TargetX, Y = task.TargetY });
                    break;
            }
            colonist.CarryingItem = null;
            colonist.CarryingCount = 0;
            world.Tasks.Remove(world, task, releaseReservations: false);
            colonist.TaskId = 0;
            colonist.Phase = HaulPhase.None;
            colonist.Activity = ColonistActivity.Idle;
        }

        private void CompleteTask(World world, Colonist colonist, WorkTask task)
        {
            world.Tasks.Remove(world, task, releaseReservations: false);
            colonist.TaskId = 0;
            colonist.WorkAccum = 0f;
            colonist.Phase = HaulPhase.None;
            colonist.Activity = ColonistActivity.Idle;
        }

        /// <summary>Drops carried goods, releases the claimed task (deleting it so the next
        /// dispatch round regenerates a clean one) and returns the colonist to Idle.</summary>
        public void AbandonCurrent(World world, Colonist colonist)
        {
            ReleaseBed(world, colonist);
            if (colonist.TaskId != 0 && world.Tasks.TryGet(colonist.TaskId, out var task))
            {
                if (colonist.CarryingCount > 0)
                {
                    world.Piles.Drop(colonist.CarryingItem, colonist.CarryingCount, colonist.X, colonist.Y);
                    colonist.CarryingItem = null;
                    colonist.CarryingCount = 0;
                    world.Tasks.Remove(world, task, releaseReservations: false);
                    world.Tasks.ReleaseDestinationReservation(world, task);
                }
                else
                {
                    world.Tasks.Remove(world, task, releaseReservations: true);
                }
            }
            colonist.TaskId = 0;
            colonist.WorkAccum = 0f;
            colonist.Phase = HaulPhase.None;
        }

        // ---------------------------------------------------------------- movement

        /// <summary>Moves one tick toward the target (or an adjacent walkable cell);
        /// returns true when arrived.</summary>
        private bool MoveToward(World world, Colonist colonist, int targetX, int targetY)
        {
            int goalX = targetX;
            int goalY = targetY;
            var ctx = world.PathContext;
            if (!ctx.IsWalkable(goalX, goalY))
            {
                if (!FindAdjacentWalkable(world, targetX, targetY, out goalX, out goalY))
                {
                    FailMovement(world, colonist);
                    return false;
                }
            }

            if (colonist.X == goalX && colonist.Y == goalY)
            {
                colonist.ClearPath();
                return true;
            }

            if (colonist.Path == null || colonist.PathGoalX != goalX || colonist.PathGoalY != goalY)
            {
                var path = Pathfinding.FindPath(ctx, colonist.X, colonist.Y, goalX, goalY);
                if (path == null)
                {
                    FailMovement(world, colonist);
                    return false;
                }
                colonist.Path = path;
                colonist.PathIndex = 0;
                colonist.PathGoalX = goalX;
                colonist.PathGoalY = goalY;
                colonist.MoveProgress = 0f;
            }

            if (colonist.PathIndex >= colonist.Path.Count - 1)
            {
                colonist.ClearPath();
                return colonist.X == goalX && colonist.Y == goalY;
            }

            var next = colonist.Path[colonist.PathIndex + 1];
            if (!ctx.CanStep(colonist.X, colonist.Y, next.x, next.y))
            {
                colonist.ClearPath();
                return false;
            }

            float speed = Balance.WalkCellsPerTick;
            if (world.Buildings.IsCellRoad(colonist.X, colonist.Y) && world.Buildings.IsCellRoad(next.x, next.y))
            {
                speed *= Balance.RoadSpeedFactor;
            }
            bool diagonal = next.x != colonist.X && next.y != colonist.Y;
            colonist.MoveProgress += speed;
            float required = diagonal ? DiagonalDistance : 1f;
            if (colonist.MoveProgress >= required)
            {
                colonist.MoveProgress -= required;
                colonist.X = next.x;
                colonist.Y = next.y;
                colonist.PathIndex++;
            }
            return false;
        }

        private void FailMovement(World world, Colonist colonist)
        {
            AbandonCurrent(world, colonist);
            colonist.StockTargetX = -1;
            colonist.StockTargetY = -1;
            colonist.WaterSearchCooldown = StockSearchCooldownTicks;
            colonist.FoodSearchCooldown = StockSearchCooldownTicks;
            colonist.Activity = ColonistActivity.Idle;
        }

        private static bool FindAdjacentWalkable(World world, int x, int y, out int outX, out int outY)
        {
            var ctx = world.PathContext;
            for (int ring = 1; ring <= 2; ring++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                        {
                            continue;
                        }
                        if (ctx.IsWalkable(x + dx, y + dy))
                        {
                            outX = x + dx;
                            outY = y + dy;
                            return true;
                        }
                    }
                }
            }
            outX = x;
            outY = y;
            return false;
        }

        internal void RestoreFrom(List<SavedColonist> saved)
        {
            _byId.Clear();
            _nextId = 1;
            foreach (var s in saved)
            {
                var colonist = new Colonist
                {
                    Id = s.Id,
                    X = s.X,
                    Y = s.Y,
                    O2 = s.O2,
                    Water = s.Water,
                    Food = s.Food,
                    Sleep = s.Sleep,
                    Temp = s.Temp,
                    BottleO2 = s.BottleO2,
                    Alive = s.Alive,
                    CriticalCause = (DeathCause)s.CriticalCause,
                    CriticalTicksLeft = s.CriticalTicksLeft,
                    FaintTicksLeft = s.FaintTicksLeft,
                    Activity = s.Alive
                        ? (s.FaintTicksLeft > 0 ? ColonistActivity.Fainted : ColonistActivity.Idle)
                        : ColonistActivity.Dead
                };
                _byId.Add(colonist.Id, colonist);
                if (s.Id >= _nextId)
                {
                    _nextId = s.Id + 1;
                }
            }
        }
    }
}
