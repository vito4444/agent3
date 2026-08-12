using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    public enum UnitSide
    {
        Player,
        Hostile
    }

    /// <summary>The only three orders (docs/plan/07: 无微操、无编队技能).</summary>
    public enum UnitOrder
    {
        Hold,
        Patrol,
        Rally
    }

    public sealed class CombatUnit
    {
        public int Id;
        public UnitSide Side;
        public int X;
        public int Y;
        public float MoveProgress;
        public List<(int x, int y)> Path;
        public int PathIndex;
        public int PathGoalX = -1;
        public int PathGoalY = -1;
        public float Hp;
        public float MaxHp;
        public float DamagePerHit;
        public int CooldownTicks;
        public UnitOrder Order = UnitOrder.Rally;
        public int HoldX;
        public int HoldY;
        public int PatrolAx;
        public int PatrolAy;
        public int PatrolBx;
        public int PatrolBy;
        public bool PatrolTowardB = true;
        /// <summary>Armored variant (phase-armor branch equipment).</summary>
        public bool Armored;

        public void ClearPath()
        {
            Path = null;
            PathIndex = 0;
            PathGoalX = -1;
            PathGoalY = -1;
            MoveProgress = 0f;
        }
    }

    public sealed class UnitDeployedEvent : ISimEvent
    {
        public int UnitId;
        public UnitSide Side;
    }

    public sealed class UnitLostEvent : ISimEvent
    {
        public int UnitId;
        public UnitSide Side;
    }

    public sealed class WaveSpawnedEvent : ISimEvent
    {
        public int Count;
        public int WaveIndex;
    }

    public sealed class WaveRepelledEvent : ISimEvent
    {
        public int RecordersDropped;
    }

    public sealed class CommandCoreDestroyedEvent : ISimEvent
    {
    }

    /// <summary>
    /// Entity-level combat inside the active region (docs/plan/07 战斗节): combat bots
    /// with the three orders, hostile waves entering from the region edge and pushing
    /// toward the command core, auto-engagement, defense buildings that shoot (sentry
    /// guns burn kinetic rounds, laser towers burn grid power), shield domes absorbing
    /// the opening barrage, and the repel/core-destroyed outcomes. Deterministic: units
    /// tick in id order, targets resolve by distance then id.
    /// </summary>
    public sealed class BattleSystem
    {
        private const float PlayerBotHp = 100f;
        private const float PlayerBotDamage = 6f;
        private const float ArmoredHpFactor = 1.6f;
        private const float HostileHp = 80f;
        private const float HostileDamage = 6f;
        private const int HitIntervalTicks = 10;
        private const int EngageRange = 6;
        private const int SentryRange = 8;
        private const float SentryDamage = 8f;
        private const int LaserRange = 10;
        private const float LaserDamage = 12f;
        private const float BuildingDamagePerHit = 4f;
        private const int ShieldBarrierTicks = 500;
        private const int WaveIntervalTicks = 2 * GameConstants.TicksPerHour;
        private const int RecorderDropDivisor = 4;
        private const int BaseGroupCap = 12;
        private const int SwarmGroupCap = 24;
        private const float DiagonalDistance = 1.41421356f;
        private const float UnitSpeedFactor = 1.2f;

        private readonly Dictionary<int, CombatUnit> _units = new Dictionary<int, CombatUnit>();
        private int _nextId = 1;

        public IReadOnlyDictionary<int, CombatUnit> Units => _units;

        public List<CombatUnit> UnitsSorted()
        {
            var ids = new List<int>(_units.Keys);
            ids.Sort();
            var result = new List<CombatUnit>(ids.Count);
            foreach (int id in ids)
            {
                result.Add(_units[id]);
            }
            return result;
        }

        public int RallyX;
        public int RallyY;
        /// <summary>Queued hostile waves: (spawnTick, count). Persisted.</summary>
        public List<(long tick, int count)> PendingWaves = new List<(long, int)>();
        /// <summary>Ticks of shield-dome barrage absorption remaining for this raid.</summary>
        public int ShieldTicksRemaining;
        /// <summary>Strength of the raid currently in progress (for the drop payout).</summary>
        public int ActiveRaidStrength;

        public int PlayerUnitCount
        {
            get
            {
                int count = 0;
                foreach (var unit in _units.Values)
                {
                    if (unit.Side == UnitSide.Player)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public bool RaidInProgress => ActiveRaidStrength > 0;

        /// <summary>编组上限 12,蜂群协议解锁后 24 (docs/plan/07 集群战术).</summary>
        public static int GroupCap(World world)
        {
            return world.Tech.IsUnlocked("branch_swarm_tactics") ? SwarmGroupCap : BaseGroupCap;
        }

        // ---------------------------------------------------------------- deployment

        /// <summary>Deploys a combat bot from stock as a live unit (armored variant when
        /// the phase-armor equipment item is used).</summary>
        public CombatUnit DeployPlayerBot(World world, int x, int y, bool armored)
        {
            if (PlayerUnitCount >= GroupCap(world))
            {
                return null;
            }
            string itemId = armored ? "armored_combat_bot" : "combat_bot";
            if (!world.TryConsumeItemAnywhere(itemId))
            {
                return null;
            }
            var unit = new CombatUnit
            {
                Id = _nextId,
                Side = UnitSide.Player,
                X = x,
                Y = y,
                MaxHp = PlayerBotHp * (armored ? ArmoredHpFactor : 1f),
                Hp = PlayerBotHp * (armored ? ArmoredHpFactor : 1f),
                DamagePerHit = PlayerBotDamage,
                Armored = armored,
                Order = UnitOrder.Hold,
                HoldX = x,
                HoldY = y
            };
            _nextId++;
            _units.Add(unit.Id, unit);
            world.Events.Add(new UnitDeployedEvent { UnitId = unit.Id, Side = unit.Side });
            return unit;
        }

        public void SetOrder(int unitId, UnitOrder order, int ax, int ay, int bx, int by)
        {
            if (!_units.TryGetValue(unitId, out var unit) || unit.Side != UnitSide.Player)
            {
                return;
            }
            unit.Order = order;
            unit.HoldX = ax;
            unit.HoldY = ay;
            unit.PatrolAx = ax;
            unit.PatrolAy = ay;
            unit.PatrolBx = bx;
            unit.PatrolBy = by;
            unit.PatrolTowardB = true;
            unit.ClearPath();
        }

        /// <summary>撤退: everyone falls back to the rally flag.</summary>
        public void RetreatAll(World world)
        {
            RallyX = world.PodInteriorX;
            RallyY = world.PodInteriorY;
            foreach (var unit in _units.Values)
            {
                if (unit.Side == UnitSide.Player)
                {
                    unit.Order = UnitOrder.Rally;
                    unit.ClearPath();
                }
            }
        }

        // ---------------------------------------------------------------- raids

        /// <summary>Queues a raid: strength split into 1-3 waves entering from the region
        /// edge every two hours (docs/plan/07 防御战).</summary>
        public void QueueRaid(World world, int strength, int waves)
        {
            ActiveRaidStrength = strength;
            waves = Math.Max(1, Math.Min(3, waves));
            int perWave = strength / waves;
            int remainder = strength - perWave * waves;
            PendingWaves.Clear();
            for (int i = 0; i < waves; i++)
            {
                PendingWaves.Add((world.Tick + (long)i * WaveIntervalTicks, perWave + (i == 0 ? remainder : 0)));
            }
            // A powered shield dome absorbs the opening barrage (docs/plan/07 防御建筑).
            foreach (var pair in world.Buildings.All)
            {
                if (pair.Value.DefId == BuildingDefs.ShieldDomeId &&
                    world.Networks.IsPowered(world, pair.Value))
                {
                    ShieldTicksRemaining = ShieldBarrierTicks;
                }
            }
        }

        private void SpawnDueWaves(World world)
        {
            for (int i = 0; i < PendingWaves.Count; i++)
            {
                if (world.Tick < PendingWaves[i].tick)
                {
                    continue;
                }
                int count = PendingWaves[i].count;
                PendingWaves.RemoveAt(i);
                i--;
                var stream = world.GetStream("raid_waves");
                for (int u = 0; u < count; u++)
                {
                    var (x, y) = EdgeCell(world, stream);
                    var unit = new CombatUnit
                    {
                        Id = _nextId,
                        Side = UnitSide.Hostile,
                        X = x,
                        Y = y,
                        MaxHp = HostileHp,
                        Hp = HostileHp,
                        DamagePerHit = HostileDamage
                    };
                    _nextId++;
                    _units.Add(unit.Id, unit);
                }
                world.Events.Add(new WaveSpawnedEvent { Count = count, WaveIndex = i + 1 });
            }
        }

        private static (int x, int y) EdgeCell(World world, Rng stream)
        {
            int size = world.Terrain.Size;
            int edge = stream.NextInt(0, 4);
            int along = stream.NextInt(1, size - 1);
            switch (edge)
            {
                case 0: return (along, 1);
                case 1: return (along, size - 2);
                case 2: return (1, along);
                default: return (size - 2, along);
            }
        }

        // ---------------------------------------------------------------- tick

        public void Tick(World world)
        {
            SpawnDueWaves(world);
            if (ShieldTicksRemaining > 0)
            {
                ShieldTicksRemaining--;
            }

            var ids = new List<int>(_units.Keys);
            ids.Sort();
            foreach (int id in ids)
            {
                if (_units.TryGetValue(id, out var unit))
                {
                    TickUnit(world, unit);
                }
            }
            TickDefenseBuildings(world);
            CheckRaidOutcome(world);
        }

        private void TickUnit(World world, CombatUnit unit)
        {
            if (unit.CooldownTicks > 0)
            {
                unit.CooldownTicks--;
            }

            if (unit.Side == UnitSide.Player)
            {
                var target = NearestUnit(unit.X, unit.Y, UnitSide.Hostile, EngageRange);
                if (target != null)
                {
                    Attack(world, unit, target);
                    return;
                }
                MoveByOrder(world, unit);
                return;
            }

            // Hostile: engage player units in range, else push toward the command core,
            // chewing through whatever building blocks the way.
            var playerTarget = NearestUnit(unit.X, unit.Y, UnitSide.Player, EngageRange);
            if (playerTarget != null)
            {
                Attack(world, unit, playerTarget);
                return;
            }
            var core = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            if (core == null)
            {
                return;
            }
            int blocking = AdjacentBlockingBuilding(world, unit);
            if (blocking != 0)
            {
                AttackBuilding(world, unit, blocking);
                return;
            }
            if (!MoveToward(world, unit, core.X, core.Y))
            {
                return;
            }
            AttackBuilding(world, unit, core.Id);
        }

        private CombatUnit NearestUnit(int x, int y, UnitSide side, int range)
        {
            CombatUnit best = null;
            int bestDistance = int.MaxValue;
            foreach (var unit in _units.Values)
            {
                if (unit.Side != side)
                {
                    continue;
                }
                int distance = Math.Max(Math.Abs(unit.X - x), Math.Abs(unit.Y - y));
                if (distance <= range &&
                    (distance < bestDistance || (distance == bestDistance && best != null && unit.Id < best.Id)))
                {
                    bestDistance = distance;
                    best = unit;
                }
            }
            return best;
        }

        private void Attack(World world, CombatUnit attacker, CombatUnit target)
        {
            if (attacker.CooldownTicks > 0)
            {
                return;
            }
            attacker.CooldownTicks = HitIntervalTicks;
            target.Hp -= attacker.DamagePerHit;
            if (target.Hp <= 0f)
            {
                _units.Remove(target.Id);
                world.Events.Add(new UnitLostEvent { UnitId = target.Id, Side = target.Side });
            }
        }

        private int AdjacentBlockingBuilding(World world, CombatUnit unit)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int id = world.Buildings.GetBuildingAt(unit.X + dx, unit.Y + dy);
                    if (id == 0 || !world.Buildings.TryGet(id, out var state) ||
                        !BuildingDefs.TryGet(state.DefId, out var def))
                    {
                        continue;
                    }
                    if (def.Kind == BuildingKind.Wall || state.DefId == "heavy_wall")
                    {
                        return id;
                    }
                }
            }
            return 0;
        }

        private void AttackBuilding(World world, CombatUnit unit, int buildingId)
        {
            if (unit.CooldownTicks > 0 || ShieldTicksRemaining > 0)
            {
                return;
            }
            if (!world.Buildings.TryGet(buildingId, out var building))
            {
                return;
            }
            unit.CooldownTicks = HitIntervalTicks;
            float damage = BuildingDamagePerHit;
            if (building.DefId == "heavy_wall")
            {
                damage /= 4f; // 重型城墙耐久 ×4 (docs/plan/07 相变装甲分支).
            }
            building.Durability -= damage;
            if (building.Durability <= 0f)
            {
                bool wasCore = BuildingDefs.TryGet(building.DefId, out var def) && def.Kind == BuildingKind.CrashPod;
                world.Buildings.Remove(buildingId);
                world.Events.Add(new BuildingRemovedEvent { BuildingId = buildingId });
                if (wasCore)
                {
                    world.Events.Add(new CommandCoreDestroyedEvent());
                }
            }
        }

        private void TickDefenseBuildings(World world)
        {
            if (world.Tick % HitIntervalTicks != 0)
            {
                return;
            }
            var ids = new List<int>(world.Buildings.All.Keys);
            ids.Sort();
            foreach (int id in ids)
            {
                if (!world.Buildings.TryGet(id, out var building))
                {
                    continue;
                }
                if (building.DefId == BuildingDefs.SentryGunId)
                {
                    var target = NearestUnit(building.X, building.Y, UnitSide.Hostile, SentryRange);
                    if (target != null && world.TryConsumeItemAnywhere("kinetic_round"))
                    {
                        Damage(world, target, SentryDamage);
                    }
                }
                else if (building.DefId == BuildingDefs.LaserTowerId &&
                         world.Networks.IsPowered(world, building))
                {
                    var target = NearestUnit(building.X, building.Y, UnitSide.Hostile, LaserRange);
                    if (target != null)
                    {
                        Damage(world, target, LaserDamage);
                    }
                }
            }
        }

        private void Damage(World world, CombatUnit target, float amount)
        {
            target.Hp -= amount;
            if (target.Hp <= 0f)
            {
                _units.Remove(target.Id);
                world.Events.Add(new UnitLostEvent { UnitId = target.Id, Side = target.Side });
            }
        }

        private void CheckRaidOutcome(World world)
        {
            if (ActiveRaidStrength <= 0 || PendingWaves.Count > 0)
            {
                return;
            }
            foreach (var unit in _units.Values)
            {
                if (unit.Side == UnitSide.Hostile)
                {
                    return;
                }
            }
            // 击退:掉落战斗记录仪 (docs/plan/07 战斗掉落).
            int recorders = Math.Max(1, ActiveRaidStrength / RecorderDropDivisor);
            world.Piles.Drop("data_recorder", recorders, world.StartX, world.StartY);
            world.Events.Add(new WaveRepelledEvent { RecordersDropped = recorders });
            ActiveRaidStrength = 0;
            ShieldTicksRemaining = 0;
        }

        // ---------------------------------------------------------------- movement

        private void MoveByOrder(World world, CombatUnit unit)
        {
            switch (unit.Order)
            {
                case UnitOrder.Hold:
                    MoveToward(world, unit, unit.HoldX, unit.HoldY);
                    break;
                case UnitOrder.Patrol:
                    int tx = unit.PatrolTowardB ? unit.PatrolBx : unit.PatrolAx;
                    int ty = unit.PatrolTowardB ? unit.PatrolBy : unit.PatrolAy;
                    if (MoveToward(world, unit, tx, ty))
                    {
                        unit.PatrolTowardB = !unit.PatrolTowardB;
                        unit.ClearPath();
                    }
                    break;
                default:
                    MoveToward(world, unit, RallyX, RallyY);
                    break;
            }
        }

        private bool MoveToward(World world, CombatUnit unit, int targetX, int targetY)
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
            if (unit.X == goalX && unit.Y == goalY)
            {
                unit.ClearPath();
                return true;
            }
            if (unit.Path == null || unit.PathGoalX != goalX || unit.PathGoalY != goalY)
            {
                var path = Pathfinding.FindPath(ctx, unit.X, unit.Y, goalX, goalY);
                if (path == null)
                {
                    return false;
                }
                unit.Path = path;
                unit.PathIndex = 0;
                unit.PathGoalX = goalX;
                unit.PathGoalY = goalY;
                unit.MoveProgress = 0f;
            }
            if (unit.PathIndex >= unit.Path.Count - 1)
            {
                unit.ClearPath();
                return unit.X == goalX && unit.Y == goalY;
            }
            var next = unit.Path[unit.PathIndex + 1];
            if (!ctx.CanStep(unit.X, unit.Y, next.x, next.y))
            {
                unit.ClearPath();
                return false;
            }
            float speed = Balance.WalkCellsPerTick * UnitSpeedFactor;
            bool diagonal = next.x != unit.X && next.y != unit.Y;
            unit.MoveProgress += speed;
            float required = diagonal ? DiagonalDistance : 1f;
            if (unit.MoveProgress >= required)
            {
                unit.MoveProgress -= required;
                unit.X = next.x;
                unit.Y = next.y;
                unit.PathIndex++;
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

        // ---------------------------------------------------------------- persistence

        internal void RestoreFrom(List<SavedCombatUnit> saved, List<SavedWave> waves,
            int rallyX, int rallyY, int shieldTicks, int raidStrength)
        {
            _units.Clear();
            _nextId = 1;
            if (saved != null)
            {
                foreach (var s in saved)
                {
                    _units.Add(s.Id, new CombatUnit
                    {
                        Id = s.Id,
                        Side = (UnitSide)s.Side,
                        X = s.X,
                        Y = s.Y,
                        Hp = s.Hp,
                        MaxHp = s.MaxHp,
                        DamagePerHit = s.DamagePerHit,
                        Order = (UnitOrder)s.Order,
                        HoldX = s.HoldX,
                        HoldY = s.HoldY,
                        PatrolAx = s.PatrolAx,
                        PatrolAy = s.PatrolAy,
                        PatrolBx = s.PatrolBx,
                        PatrolBy = s.PatrolBy,
                        Armored = s.Armored
                    });
                    if (s.Id >= _nextId)
                    {
                        _nextId = s.Id + 1;
                    }
                }
            }
            PendingWaves.Clear();
            if (waves != null)
            {
                foreach (var wave in waves)
                {
                    PendingWaves.Add((wave.Tick, wave.Count));
                }
            }
            RallyX = rallyX;
            RallyY = rallyY;
            ShieldTicksRemaining = shieldTicks;
            ActiveRaidStrength = raidStrength;
        }
    }
}
