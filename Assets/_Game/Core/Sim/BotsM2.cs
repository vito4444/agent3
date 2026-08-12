using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    public enum BotState
    {
        Idle,
        Working,
        GoingToCharge,
        Charging
    }

    public sealed class Bot
    {
        public int Id;
        public int HomeStationId;
        public int X;
        public int Y;
        public float MoveProgress;
        public List<(int x, int y)> Path;
        public int PathIndex;
        public int PathGoalX = -1;
        public int PathGoalY = -1;
        public float Battery = Balance.BotBatteryMax;
        public string CarryingItem;
        public int CarryingCount;
        public int TaskId;
        public HaulPhase Phase = HaulPhase.None;
        public BotState State = BotState.Idle;
        public int ChargePostId;

        public bool Operational => Battery > Balance.BotSeekChargeAt || State == BotState.Charging;

        public void ClearPath()
        {
            Path = null;
            PathIndex = 0;
            PathGoalX = -1;
            PathGoalY = -1;
            MoveProgress = 0f;
        }

        public void AssignTask(WorkTask task)
        {
            TaskId = task.Id;
            State = BotState.Working;
            Phase = HaulPhase.ToSource;
            ClearPath();
        }
    }

    /// <summary>
    /// Hauler bots (docs/plan/02: 搬运蛛 — ground haulers, recharge at charging posts).
    /// Each bot station hosts up to 4 bots; while at least one bot is operational, haul
    /// tasks stop entering the human labor pool (docs/plan/03 hand-to-machine mapping,
    /// M2-T9). Bots drain battery while active and queue at powered charging posts.
    /// </summary>
    public sealed class BotSystem
    {
        public const int BotsPerStation = 4;
        private const float DiagonalDistance = 1.41421356f;

        private readonly Dictionary<int, Bot> _byId = new Dictionary<int, Bot>();
        private int _nextId = 1;

        public IReadOnlyDictionary<int, Bot> All => _byId;

        public List<Bot> AllSorted()
        {
            var keys = new List<int>(_byId.Keys);
            keys.Sort();
            var result = new List<Bot>(keys.Count);
            foreach (int key in keys)
            {
                result.Add(_byId[key]);
            }
            return result;
        }

        public bool TryGet(int id, out Bot bot) => _byId.TryGetValue(id, out bot);

        public bool AnyOperational()
        {
            foreach (var bot in _byId.Values)
            {
                if (bot.Operational)
                {
                    return true;
                }
            }
            return false;
        }

        public List<Bot> IdleReady()
        {
            var result = new List<Bot>();
            foreach (var bot in AllSorted())
            {
                if (bot.State == BotState.Idle && bot.Battery > Balance.BotSeekChargeAt)
                {
                    result.Add(bot);
                }
            }
            return result;
        }

        public void Tick(World world)
        {
            foreach (var bot in AllSorted())
            {
                TickBot(world, bot);
            }
        }

        /// <summary>Every completed bot station hosts its full complement. Runs before
        /// dispatch so freshly built stations take over hauls the same round.</summary>
        public void EnsureStationBots(World world)
        {
            foreach (var pair in world.Buildings.All)
            {
                if (!BuildingDefs.TryGet(pair.Value.DefId, out var def) || def.Kind != BuildingKind.BotStation)
                {
                    continue;
                }
                int count = 0;
                foreach (var bot in _byId.Values)
                {
                    if (bot.HomeStationId == pair.Key)
                    {
                        count++;
                    }
                }
                for (int i = count; i < BotsPerStation; i++)
                {
                    var bot = new Bot
                    {
                        Id = _nextId,
                        HomeStationId = pair.Key,
                        X = pair.Value.X,
                        Y = pair.Value.Y
                    };
                    _nextId++;
                    _byId.Add(bot.Id, bot);
                }
            }
            // Bots whose home station is gone shut down and are removed.
            var stale = new List<int>();
            foreach (var pair in _byId)
            {
                if (!world.Buildings.TryGet(pair.Value.HomeStationId, out _))
                {
                    stale.Add(pair.Key);
                }
            }
            foreach (int id in stale)
            {
                var bot = _byId[id];
                if (bot.TaskId != 0 && world.Tasks.TryGet(bot.TaskId, out var task))
                {
                    world.Tasks.Remove(world, task, releaseReservations: bot.CarryingCount == 0);
                    if (bot.CarryingCount > 0)
                    {
                        world.Piles.Drop(bot.CarryingItem, bot.CarryingCount, bot.X, bot.Y);
                    }
                }
                _byId.Remove(id);
            }
        }

        private void TickBot(World world, Bot bot)
        {
            switch (bot.State)
            {
                case BotState.Idle:
                    break;

                case BotState.Working:
                    if (bot.Battery <= Balance.BotSeekChargeAt)
                    {
                        AbandonTask(world, bot);
                        bot.State = BotState.GoingToCharge;
                        bot.ClearPath();
                        break;
                    }
                    Drain(bot);
                    TickHaul(world, bot);
                    break;

                case BotState.GoingToCharge:
                    Drain(bot);
                    TickGoingToCharge(world, bot);
                    break;

                case BotState.Charging:
                    TickCharging(world, bot);
                    break;
            }
        }

        private static void Drain(Bot bot)
        {
            bot.Battery = Math.Max(0f, bot.Battery - Balance.BotBatteryDrainPerHourActive / GameConstants.TicksPerHour);
        }

        private void TickGoingToCharge(World world, Bot bot)
        {
            if (bot.ChargePostId == 0 || !world.Buildings.TryGet(bot.ChargePostId, out var post))
            {
                post = FindPoweredChargePost(world);
                if (post == null)
                {
                    bot.State = BotState.Idle;
                    return;
                }
                bot.ChargePostId = post.Id;
            }
            if (MoveToward(world, bot, post.X, post.Y))
            {
                bot.State = BotState.Charging;
            }
        }

        private void TickCharging(World world, Bot bot)
        {
            if (!world.Buildings.TryGet(bot.ChargePostId, out var post) ||
                !world.Networks.IsPowered(world, post))
            {
                bot.ChargePostId = 0;
                bot.State = BotState.Idle;
                return;
            }
            bot.Battery = Math.Min(Balance.BotBatteryMax, bot.Battery + Balance.BotChargePerHour / GameConstants.TicksPerHour);
            if (bot.Battery >= Balance.BotBatteryMax)
            {
                bot.ChargePostId = 0;
                bot.State = BotState.Idle;
            }
        }

        private static BuildingState FindPoweredChargePost(World world)
        {
            BuildingState best = null;
            foreach (var pair in world.Buildings.All)
            {
                if (BuildingDefs.TryGet(pair.Value.DefId, out var def) && def.Kind == BuildingKind.ChargingPost &&
                    world.Networks.IsPowered(world, pair.Value) &&
                    (best == null || pair.Key < best.Id))
                {
                    best = pair.Value;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- hauling

        private void TickHaul(World world, Bot bot)
        {
            if (!world.Tasks.TryGet(bot.TaskId, out var task))
            {
                bot.TaskId = 0;
                bot.State = BotState.Idle;
                return;
            }

            if (bot.Phase == HaulPhase.ToSource)
            {
                if (!SourceStillValid(world, task))
                {
                    world.Tasks.Remove(world, task, releaseReservations: true);
                    Finish(bot);
                    return;
                }
                GetSourceCell(world, task, out int sx, out int sy);
                if (!MoveToward(world, bot, sx, sy))
                {
                    return;
                }
                PickUp(world, bot, task);
                return;
            }

            if (!DestinationStillValid(world, task))
            {
                if (bot.CarryingCount > 0)
                {
                    world.Piles.Drop(bot.CarryingItem, bot.CarryingCount, bot.X, bot.Y);
                    bot.CarryingItem = null;
                    bot.CarryingCount = 0;
                }
                world.Tasks.Remove(world, task, releaseReservations: false);
                Finish(bot);
                return;
            }
            if (!MoveToward(world, bot, task.TargetX, task.TargetY))
            {
                return;
            }
            Deliver(world, bot, task);
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

        private static bool DestinationStillValid(World world, WorkTask task)
        {
            switch (task.Type)
            {
                case TaskType.HaulToBlueprint:
                    return world.Blueprints.TryGet(task.BlueprintId, out _);
                case TaskType.HaulToStation:
                    return world.Buildings.TryGet(task.StationId, out _);
                case TaskType.HaulToStorage:
                    return world.Buildings.TryGet(task.BuildingId, out _);
                default:
                    return false;
            }
        }

        private void PickUp(World world, Bot bot, WorkTask task)
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
                Finish(bot);
                return;
            }
            bot.CarryingItem = task.ItemId;
            bot.CarryingCount = taken;
            bot.Phase = HaulPhase.ToDest;
            bot.ClearPath();
        }

        private void Deliver(World world, Bot bot, WorkTask task)
        {
            int count = bot.CarryingCount;
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
            }
            bot.CarryingItem = null;
            bot.CarryingCount = 0;
            world.Tasks.Remove(world, task, releaseReservations: false);
            Finish(bot);
        }

        private void AbandonTask(World world, Bot bot)
        {
            if (bot.TaskId != 0 && world.Tasks.TryGet(bot.TaskId, out var task))
            {
                if (bot.CarryingCount > 0)
                {
                    world.Piles.Drop(bot.CarryingItem, bot.CarryingCount, bot.X, bot.Y);
                    bot.CarryingItem = null;
                    bot.CarryingCount = 0;
                    world.Tasks.Remove(world, task, releaseReservations: false);
                    world.Tasks.ReleaseDestinationReservation(world, task);
                }
                else
                {
                    world.Tasks.Remove(world, task, releaseReservations: true);
                }
            }
            bot.TaskId = 0;
            bot.Phase = HaulPhase.None;
        }

        private static void Finish(Bot bot)
        {
            bot.TaskId = 0;
            bot.Phase = HaulPhase.None;
            bot.State = BotState.Idle;
        }

        private static bool MoveToward(World world, Bot bot, int targetX, int targetY)
        {
            var ctx = world.PathContext;
            int goalX = targetX;
            int goalY = targetY;
            if (!ctx.IsWalkable(goalX, goalY))
            {
                if (!FindAdjacentWalkable(ctx, targetX, targetY, out goalX, out goalY))
                {
                    return false;
                }
            }
            if (bot.X == goalX && bot.Y == goalY)
            {
                bot.ClearPath();
                return true;
            }
            if (bot.Path == null || bot.PathGoalX != goalX || bot.PathGoalY != goalY)
            {
                var path = Pathfinding.FindPath(ctx, bot.X, bot.Y, goalX, goalY);
                if (path == null)
                {
                    return false;
                }
                bot.Path = path;
                bot.PathIndex = 0;
                bot.PathGoalX = goalX;
                bot.PathGoalY = goalY;
                bot.MoveProgress = 0f;
            }
            if (bot.PathIndex >= bot.Path.Count - 1)
            {
                bot.ClearPath();
                return bot.X == goalX && bot.Y == goalY;
            }
            var next = bot.Path[bot.PathIndex + 1];
            if (!ctx.CanStep(bot.X, bot.Y, next.x, next.y))
            {
                bot.ClearPath();
                return false;
            }
            float speed = Balance.WalkCellsPerTick * Balance.BotSpeedFactor;
            if (world.Buildings.IsCellRoad(bot.X, bot.Y) && world.Buildings.IsCellRoad(next.x, next.y))
            {
                speed *= Balance.RoadSpeedFactor;
            }
            bool diagonal = next.x != bot.X && next.y != bot.Y;
            bot.MoveProgress += speed;
            float required = diagonal ? DiagonalDistance : 1f;
            if (bot.MoveProgress >= required)
            {
                bot.MoveProgress -= required;
                bot.X = next.x;
                bot.Y = next.y;
                bot.PathIndex++;
            }
            return false;
        }

        private static bool FindAdjacentWalkable(Pathfinding.Context ctx, int x, int y, out int outX, out int outY)
        {
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

        internal void RestoreFrom(List<SavedBot> saved)
        {
            _byId.Clear();
            _nextId = 1;
            if (saved == null)
            {
                return;
            }
            foreach (var s in saved)
            {
                _byId.Add(s.Id, new Bot
                {
                    Id = s.Id,
                    HomeStationId = s.HomeStationId,
                    X = s.X,
                    Y = s.Y,
                    Battery = s.Battery
                });
                if (s.Id >= _nextId)
                {
                    _nextId = s.Id + 1;
                }
            }
        }
    }
}
