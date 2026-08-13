using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M7-T2/T3 entity layer: three orders, group caps, edge waves, auto
    /// engagement, defense buildings shooting, repel drops and core-destruction loss.</summary>
    public sealed class BattleTests
    {
        private static World NewBattleWorld(ulong seed, int bots)
        {
            var world = TestUtil.NewColonyWorld(seed, 96);
            world.Tech.UnlockAll();
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add("combat_bot", bots);
            world.Battle.RallyX = world.PodInteriorX;
            world.Battle.RallyY = world.PodInteriorY;
            return world;
        }

        [Test]
        public void ThreeOrders_Hold_Patrol_Rally_AllMoveUnits()
        {
            var world = NewBattleWorld(201UL, 4);
            var hold = world.Battle.DeployPlayerBot(world, world.StartX - 4, world.StartY - 4, false);
            var patrol = world.Battle.DeployPlayerBot(world, world.StartX - 4, world.StartY - 2, false);
            var rally = world.Battle.DeployPlayerBot(world, world.StartX - 4, world.StartY, false);
            Assert.IsNotNull(hold);
            Assert.IsNotNull(patrol);
            Assert.IsNotNull(rally);

            world.Battle.SetOrder(hold.Id, UnitOrder.Hold, world.StartX + 10, world.StartY, 0, 0);
            world.Battle.SetOrder(patrol.Id, UnitOrder.Patrol,
                world.StartX - 8, world.StartY - 8, world.StartX - 8, world.StartY + 8);
            world.Battle.SetOrder(rally.Id, UnitOrder.Rally, 0, 0, 0, 0);
            world.Battle.RallyX = world.StartX + 6;
            world.Battle.RallyY = world.StartY + 6;

            TestUtil.Run(world, 600);
            Assert.AreEqual((world.StartX + 10, world.StartY), (hold.X, hold.Y), "驻守单位到位并停住");
            Assert.LessOrEqual(System.Math.Abs(rally.X - (world.StartX + 6)) +
                               System.Math.Abs(rally.Y - (world.StartY + 6)), 2, "集结单位跟随集结旗");
            // Patrol keeps oscillating: track the visited cells over a window.
            var visited = new System.Collections.Generic.HashSet<(int, int)>();
            for (int i = 0; i < 400; i++)
            {
                world.Step();
                visited.Add((patrol.X, patrol.Y));
            }
            Assert.Greater(visited.Count, 4, "巡逻单位在两点间持续往返");
        }

        [Test]
        public void GroupCap_Twelve_ThenTwentyFour_WithSwarmProtocol()
        {
            var world = NewBattleWorld(202UL, 40);
            // Burn the swarm branch first so it does not gate; then re-lock by using a
            // fresh world for the base case.
            var baseWorld = NewBattleWorld(203UL, 40);
            // Base world: tech tree loaded but swarm branch NOT unlocked → cap 12.
            var freshWorld = TestUtil.NewColonyWorld(204UL, 96);
            var pod = freshWorld.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add("combat_bot", 40);
            int deployed = 0;
            for (int i = 0; i < 30; i++)
            {
                if (freshWorld.Battle.DeployPlayerBot(freshWorld, freshWorld.StartX - 4, freshWorld.StartY + i % 8, false) != null)
                {
                    deployed++;
                }
            }
            Assert.AreEqual(12, deployed, "编组上限 12 (docs/plan/07)");

            // Swarm protocol unlocked → 24.
            freshWorld.Tech.UnlockBranch("branch_swarm_tactics");
            for (int i = 0; i < 30; i++)
            {
                if (freshWorld.Battle.DeployPlayerBot(freshWorld, freshWorld.StartX - 6, freshWorld.StartY + i % 8, false) != null)
                {
                    deployed++;
                }
            }
            Assert.AreEqual(24, deployed, "蜂群协议解锁后上限 24 (M7-T2)");
            _ = world;
            _ = baseWorld;
        }

        [Test]
        public void Raid_EntersFromEdge_DefendersRepel_RecordersDrop()
        {
            var world = NewBattleWorld(205UL, 12);
            // A defensive line: bots + sentry ammo.
            for (int i = 0; i < 8; i++)
            {
                world.Battle.DeployPlayerBot(world, world.StartX - 6 + i, world.StartY - 6, false);
            }
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add("kinetic_round", 200);
            world.Buildings.Place(BuildingDefs.SentryGunId, world.StartX + 3, world.StartY + 3, 0, out _);
            world.Buildings.Place(BuildingDefs.SentryGunId, world.StartX - 3, world.StartY + 3, 0, out _);

            world.Battle.QueueRaid(world, 10, 1);
            bool spawned = false;
            bool repelled = false;
            int recorders = 0;
            for (int i = 0; i < GameConstants.TicksPerDay && !repelled; i++)
            {
                world.Step();
                foreach (var evt in world.Events)
                {
                    if (evt is WaveSpawnedEvent wave)
                    {
                        spawned = true;
                        Assert.Greater(wave.Count, 0);
                        foreach (var unit in world.Battle.UnitsSorted())
                        {
                            if (unit.Side == UnitSide.Hostile)
                            {
                                bool onEdge = unit.X <= 2 || unit.Y <= 2 ||
                                              unit.X >= world.Terrain.Size - 3 || unit.Y >= world.Terrain.Size - 3;
                                Assert.IsTrue(onEdge, "波次必须从区域边缘进入 (M7-T3)");
                            }
                        }
                    }
                    if (evt is WaveRepelledEvent done)
                    {
                        repelled = true;
                        recorders = done.RecordersDropped;
                    }
                }
            }
            Assert.IsTrue(spawned, "波次生成");
            Assert.IsTrue(repelled, "防线必须击退 10 强度袭击");
            Assert.Greater(recorders, 0, "击退掉落战斗记录仪 (M7-T3)");
            Assert.Greater(world.Piles.CountOf("data_recorder"), 0, "记录仪落地");
        }

        [Test]
        public void UndefendedRaid_DestroysCore_TriggersEvent()
        {
            var world = NewBattleWorld(206UL, 0);
            world.Battle.QueueRaid(world, 24, 1);
            bool coreDown = false;
            for (int i = 0; i < GameConstants.TicksPerDay * 2 && !coreDown; i++)
            {
                world.Step();
                foreach (var evt in world.Events)
                {
                    if (evt is CommandCoreDestroyedEvent)
                    {
                        coreDown = true;
                    }
                }
            }
            Assert.IsTrue(coreDown, "无防御的核心必须被摧毁 (M7-T3 失区触发)");
            Assert.IsNull(world.Buildings.FindFirstOfKind(BuildingKind.CrashPod), "核心成为废墟");
        }

        [Test]
        public void CombatUnits_SurviveSaveRoundtrip()
        {
            var world = NewBattleWorld(207UL, 4);
            var unit = world.Battle.DeployPlayerBot(world, world.StartX - 4, world.StartY, false);
            world.Battle.SetOrder(unit.Id, UnitOrder.Patrol,
                world.StartX - 8, world.StartY, world.StartX + 8, world.StartY);
            world.Battle.QueueRaid(world, 6, 2);
            TestUtil.Run(world, 50);

            var restored = SaveSerializer.Restore(
                SaveSerializer.FromGzipJson(SaveSerializer.ToGzipJson(SaveSerializer.Capture(world))));
            Assert.AreEqual(world.Battle.Units.Count, restored.Battle.Units.Count, "单位入档 (save v3)");
            Assert.AreEqual(world.Battle.PendingWaves.Count, restored.Battle.PendingWaves.Count, "波次入档");
            Assert.AreEqual(world.Battle.ActiveRaidStrength, restored.Battle.ActiveRaidStrength);
        }
    }
}
