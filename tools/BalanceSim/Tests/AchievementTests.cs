using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M8-T1 (Core half): the 20-achievement list is complete and key triggers
    /// fire from real state; unlocks persist through universe saves.</summary>
    public sealed class AchievementTests
    {
        [Test]
        public void TwentyAchievements_Defined()
        {
            Assert.AreEqual(20, AchievementSystem.All.Length, "docs/plan/10 locked list has 20");
        }

        [Test]
        public void FirstNight_Powered_FirstContact_Unlock()
        {
            var universe = TestUtil.NewUniverse(191UL, 96);
            universe.FactionLayerVisible = true;
            var world = universe.ActiveWorld;
            world.Tech.UnlockAll();
            world.Buildings.Place(BuildingDefs.PowerPylonId, world.StartX, world.StartY + 10, 0, out _);
            world.Buildings.Place(BuildingDefs.SolarPanelId, world.StartX + 2, world.StartY + 10, 0, out _);

            for (long i = 0; i < GameConstants.TicksPerDay + GameConstants.TicksPerHour; i++)
            {
                universe.Step();
            }
            Assert.IsTrue(universe.Achievements.Unlocked.Contains("first_night"), "存活 1 游戏日");
            Assert.IsTrue(universe.Achievements.Unlocked.Contains("powered_up"), "首次电网供电");
            Assert.IsTrue(universe.Achievements.Unlocked.Contains("first_contact"), "通讯点亮势力层");
        }

        [Test]
        public void VictoryAchievements_MirrorVictoryPath()
        {
            var universe = TestUtil.NewUniverse(192UL, 96);
            universe.ActiveWorld.Buildings.Place(BuildingDefs.WarpBeaconId,
                universe.ActiveWorld.StartX + 8, universe.ActiveWorld.StartY + 8, 0, out _);
            universe.Combat.CheckVictory(universe);
            universe.Achievements.ObserveEvents(universe);
            Assert.IsTrue(universe.Achievements.Unlocked.Contains("warp_ignition"), "跃迁点火");
        }

        [Test]
        public void Unlocks_PersistThroughUniverseSave()
        {
            var universe = TestUtil.NewUniverse(193UL, 96);
            for (long i = 0; i < GameConstants.TicksPerDay + GameConstants.TicksPerHour; i++)
            {
                universe.Step();
            }
            Assert.IsTrue(universe.Achievements.Unlocked.Contains("first_night"));
            var loaded = Universe.FromGzipJson(universe.ToGzipJson());
            Assert.IsTrue(loaded.Achievements.Unlocked.Contains("first_night"),
                "成就解锁必须入宇宙档");
        }
    }
}
