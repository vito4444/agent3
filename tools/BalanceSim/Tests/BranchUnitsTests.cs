using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>集群战术 branch units (docs/plan/07): breacher bots (one-shot charges),
    /// jammer drones (turret targeting −50%), forward assembly nest (siege pressure) in
    /// the sandbox assault model, and the shock cannon knockback turret in the entity
    /// battle layer. Red Banner home defense in these tests: 300 × 0.6 × 1.2 = 216.</summary>
    public sealed class BranchUnitsTests
    {
        [Test]
        public void BreacherBots_OneShotCharges_TipTheAssault()
        {
            var universe = TestUtil.NewUniverse(221UL, 96);
            // 98 bots: 215.6 attack < 216 defense → fail.
            Assert.IsFalse(universe.Combat.Assault(universe, FactionSystem.RedBannerId, "redridge",
                combatBots: 98, targetShieldFirst: false), "无自爆蛛: 215.6 < 216 攻坚失败");
            // Same bots + 2 breachers: 215.6 + 13.2 = 228.8 > 216 → win.
            Assert.IsTrue(universe.Combat.Assault(universe, FactionSystem.RedBannerId, "redridge",
                combatBots: 98, targetShieldFirst: false, breacherBots: 2),
                "自爆蛛一次性爆破 (3× 战斗蛛) 补足攻坚差额 (branch_swarm_tactics_2)");
        }

        [Test]
        public void JammerDrones_HalveTurretTargeting_QuarterOffDefense()
        {
            var universe = TestUtil.NewUniverse(222UL, 96);
            // 75 bots: 165 < 216 → fail without jammers.
            Assert.IsFalse(universe.Combat.Assault(universe, FactionSystem.RedBannerId, "redridge",
                combatBots: 75, targetShieldFirst: false), "无干扰: 165 < 216 攻坚失败");
            // Jammers cut the turret half of defense by 50%: 216 × 0.75 = 162 < 165 → win.
            Assert.IsTrue(universe.Combat.Assault(universe, FactionSystem.RedBannerId, "redridge",
                combatBots: 75, targetShieldFirst: false, jammerDrones: 1),
                "干扰无人机使炮塔索敌 -50% (防御 ×0.75) (branch_swarm_tactics_3)");
        }

        [Test]
        public void ForwardNest_FailedAssault_KeepsSiegePressure()
        {
            var universe = TestUtil.NewUniverse(223UL, 96);
            var faction = universe.FactionsSandbox.Get(FactionSystem.RedBannerId);
            var pod = universe.ActiveWorld.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add("orbital_penetrator", 6);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(universe.Combat.Bombard(universe, "redridge"));
            }
            float weakened = universe.Combat.EffectiveDefense(universe, faction, "redridge");
            Assert.AreEqual(216f * 0.7f, weakened, 0.01f, "3 发钻地弹 → 防御 -30%");

            // Failed assault WITHOUT a nest: defense recovers one bombardment step.
            Assert.IsFalse(universe.Combat.Assault(universe, FactionSystem.RedBannerId, "redridge",
                combatBots: 10, targetShieldFirst: false));
            Assert.AreEqual(216f * 0.8f, universe.Combat.EffectiveDefense(universe, faction, "redridge"),
                0.01f, "无装配巢: 失败后防御回复 10% (docs/plan/07 进攻战 3)");

            // Failed assault WITH a nest: pressure holds, no recovery.
            Assert.IsFalse(universe.Combat.Assault(universe, FactionSystem.RedBannerId, "redridge",
                combatBots: 10, targetShieldFirst: false, forwardNest: true));
            Assert.AreEqual(216f * 0.8f, universe.Combat.EffectiveDefense(universe, faction, "redridge"),
                0.01f, "前线装配巢: 失败后防御不回复 (战地工坊维持围攻) (branch_swarm_tactics_4)");
        }

        [Test]
        public void ShockCannon_KnocksHostilesBack_ThreeTiles()
        {
            var world = TestUtil.NewColonyWorld(224UL, 96);
            world.Tech.UnlockAll();
            int cx = world.StartX, cy = world.StartY;

            // Powered shock cannon: pylon + 3 solar panels inside cover radius (start 8AM).
            world.Buildings.Place(BuildingDefs.PowerPylonId, cx - 8, cy - 8, 0, out var pylonErr);
            Assert.AreEqual(PlacementError.None, pylonErr);
            for (int i = 0; i < 3; i++)
            {
                world.Buildings.Place(BuildingDefs.SolarPanelId, cx - 11 + i * 2, cy - 10, 0, out var solarErr);
                Assert.AreEqual(PlacementError.None, solarErr);
            }
            int cannonId = world.Buildings.Place(BuildingDefs.ShockCannonId, cx - 7, cy - 6, 0, out var cannonErr);
            Assert.AreEqual(PlacementError.None, cannonErr);
            Assert.Greater(cannonId, 0);
            world.Buildings.TryGet(cannonId, out var cannon);

            // Let the grid settle, then spawn a raid and park one hostile beside the cannon.
            TestUtil.Run(world, 30);
            Assert.IsTrue(world.Networks.IsPowered(world, cannon), "震荡炮供电就绪");
            world.Battle.QueueRaid(world, 4, 1);
            CombatUnit hostile = null;
            for (int i = 0; i < 2000 && hostile == null; i++)
            {
                world.Step();
                foreach (var unit in world.Battle.UnitsSorted())
                {
                    if (unit.Side == UnitSide.Hostile)
                    {
                        hostile = unit;
                    }
                }
            }
            Assert.IsNotNull(hostile, "袭击单位入场");
            hostile.Hp = 10000f; // keep it alive through the volley
            hostile.X = cannon.X + 2;
            hostile.Y = cannon.Y;
            hostile.ClearPath();

            // Natural movement is ≤1 tile per tick; only the shock volley jumps ≥3 tiles.
            bool knocked = false;
            for (int i = 0; i < 60 && !knocked; i++)
            {
                int beforeX = hostile.X, beforeY = hostile.Y;
                world.Step();
                int jump = System.Math.Abs(hostile.X - beforeX) + System.Math.Abs(hostile.Y - beforeY);
                if (jump >= ShockJumpTiles)
                {
                    knocked = true;
                }
            }
            Assert.IsTrue(knocked, "震荡炮范围击退: 敌单位单 tick 位移 ≥3 格 (branch_swarm_tactics_5)");
        }

        private const int ShockJumpTiles = 3;
    }
}
