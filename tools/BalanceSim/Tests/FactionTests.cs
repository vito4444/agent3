using System.Collections.Generic;
using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M6-T1/T2/T8: growth formula precision, expansion pacing per docs/plan/07,
    /// personality differences, friction chain and sandbox persistence.</summary>
    public sealed class FactionTests
    {
        /// <summary>Runs the sandbox alone for N game days (star-map ticks without the
        /// heavy colony sim), mirroring Universe.Step's hourly cadence.</summary>
        private static Universe RunSandbox(int days, out List<(long hour, string faction, string body)> expansions)
        {
            var universe = TestUtil.NewUniverse(151UL, 64);
            var log = new List<(long, string, string)>();
            for (long hour = 1; hour <= (long)days * GameConstants.HoursPerDay; hour++)
            {
                universe.ActiveWorld.Events.Clear();
                universe.FactionsSandbox.HourlyTick(universe, universe.PlayerBodies(), hour);
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is FactionExpandedEvent expanded)
                    {
                        log.Add((hour, expanded.FactionId, expanded.BodyId));
                    }
                }
            }
            expansions = log;
            return universe;
        }

        [Test]
        public void PowerGrowth_MatchesFormula_Exactly()
        {
            // docs/plan/07 原文: P += 1.2 × growthMult × heldBodies^0.7 (每星图刻).
            var system = new FactionSystem();
            system.InitDefault();
            var universe = TestUtil.NewUniverse(152UL, 64);
            var redBanner = system.Get(FactionSystem.RedBannerId);
            float expected = redBanner.P;
            const int ticks = 100;
            for (int i = 0; i < ticks; i++)
            {
                expected += 1.2f * 1.3f * 1f; // 1 held body → 1^0.7 = 1.
            }
            for (int i = 1; i <= ticks; i++)
            {
                system.HourlyTick(universe, new System.Collections.Generic.HashSet<string> { "dustloam" }, i);
            }
            // Expansion during the window would deduct P; 100 hours < first conquest.
            Assert.AreEqual(expected, system.Get(FactionSystem.RedBannerId).P, 0.01f,
                "P after 100 star-map ticks must match the plan formula exactly (M6-T1)");
            Assert.AreEqual(1f + 100 * 0.001f * 0.8f, system.Get(FactionSystem.RedBannerId).T, 0.0001f,
                "T growth must match 0.001 × techMult per tick");
        }

        [Test]
        public void ExpansionPacing_MatchesPlanReference()
        {
            var universe = RunSandbox(42, out var expansions);

            long FlintDay = -1, PalewatchDay = -1, CinderDay = -1, MistDay = -1;
            foreach (var (hour, faction, body) in expansions)
            {
                long day = hour / GameConstants.HoursPerDay;
                if (faction == FactionSystem.RedBannerId && body == "flintfield") FlintDay = day;
                if (faction == FactionSystem.RedBannerId && body == "palewatch") PalewatchDay = day;
                if (faction == FactionSystem.RedBannerId && body == "cinderrock") CinderDay = day;
                if (faction == FactionSystem.MerchantId && body == "mistwatch") MistDay = day;
            }

            // docs/plan/07 步调参考: 赤旗 ~14日燧砾带、~28日苍卫、~38日灼岩;商盟 ~17日雾卫。
            Assert.That(FlintDay, Is.InRange(12, 16), "赤旗占燧砾带的步调 (计划 ~14 日)");
            Assert.That(PalewatchDay, Is.InRange(25, 31), "赤旗占苍卫的步调 (计划 ~28 日)");
            Assert.That(CinderDay, Is.InRange(35, 41), "赤旗占灼岩的步调 (计划 ~38 日)");
            Assert.That(MistDay, Is.InRange(15, 20), "商盟占雾卫的步调 (计划 ~17 日)");

            var silent = universe.FactionsSandbox.Get(FactionSystem.SilentId);
            Assert.AreEqual(1, silent.HeldBodies.Count, "静默会永不扩张 (docs/plan/07)");
            Assert.Greater(silent.T, universe.FactionsSandbox.Get(FactionSystem.RedBannerId).T,
                "封闭型科技成长最快");
        }

        [Test]
        public void RedBannerFriction_Ultimatums_ThenWar_With24hWarning()
        {
            var universe = TestUtil.NewUniverse(153UL, 64);
            var redBanner = universe.FactionsSandbox.Get(FactionSystem.RedBannerId);
            // Simulate the encirclement: red banner already holds the player's neighbor.
            redBanner.HeldBodies.Add("flintfield");

            long firstUltimatumTick = -1;
            int ultimatums = 0;
            for (long i = 0; i < 8L * GameConstants.TicksPerDay && ultimatums < 1; i++)
            {
                universe.Step();
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is FactionUltimatumEvent ultimatum)
                    {
                        ultimatums++;
                        firstUltimatumTick = universe.Tick;
                        Assert.AreEqual((int)(redBanner.P * 0.2f), ultimatum.DemandCredits, 1,
                            "通牒金额 = P × 0.2 (docs/plan/07)");
                    }
                }
            }
            Assert.AreEqual(FactionStance.Tense, redBanner.Stance, "邻接必须先进入紧张");
            Assert.AreEqual(1, ultimatums, "3 日内应收到首份通牒");

            universe.FactionsSandbox.RejectUltimatum(universe);
            Assert.AreEqual(FactionStance.Tense, redBanner.Stance, "第一次拒绝仍是紧张");
            universe.FactionsSandbox.RejectUltimatum(universe);
            Assert.AreEqual(FactionStance.War, redBanner.Stance, "第二次拒绝进入战争 (M6-T8)");
            Assert.AreEqual(universe.Tick / GameConstants.TicksPerHour + 24L, redBanner.RaidsBeginHour,
                "袭击预警恰为 24 游戏时");
        }

        [Test]
        public void Sandbox_SurvivesUniverseSaveLoad()
        {
            var universe = TestUtil.NewUniverse(154UL, 64);
            for (int i = 0; i < GameConstants.TicksPerHour * 10; i++)
            {
                universe.Step();
            }
            var before = universe.FactionsSandbox.Get(FactionSystem.RedBannerId);
            var loaded = Universe.FromGzipJson(universe.ToGzipJson());
            var after = loaded.FactionsSandbox.Get(FactionSystem.RedBannerId);
            Assert.AreEqual(before.P, after.P, 0.001f, "P must persist (M6-T1 全状态入存档)");
            Assert.AreEqual(before.T, after.T, 0.0001f);
            Assert.AreEqual(before.HeldBodies.Count, after.HeldBodies.Count);
            Assert.AreEqual(before.Stance, after.Stance);
            Assert.AreEqual(before.AttitudeToPlayer, after.AttitudeToPlayer);
        }
    }
}
