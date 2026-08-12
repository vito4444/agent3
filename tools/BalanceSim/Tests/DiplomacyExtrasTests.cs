using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>Follow-up gap closures: ultimatum payment, merchant cold shoulder,
    /// route insurance ability, faction params from CSV.</summary>
    public sealed class DiplomacyExtrasTests
    {
        [Test]
        public void PayingUltimatum_DrainsCredits_ResetsClock()
        {
            var universe = TestUtil.NewUniverse(211UL, 96);
            var redBanner = universe.FactionsSandbox.Get(FactionSystem.RedBannerId);
            redBanner.HeldBodies.Add("flintfield");
            for (long i = 0; i < 4L * GameConstants.TicksPerDay; i++)
            {
                universe.Step();
            }
            Assert.AreEqual(FactionStance.Tense, redBanner.Stance);
            universe.FactionsSandbox.RejectUltimatum(universe);
            Assert.AreEqual(1, redBanner.UltimatumsRejected);

            universe.PlayerCredits = 10000;
            Assert.IsTrue(universe.FactionsSandbox.PayUltimatum(universe), "有钱必须可以交保护费");
            Assert.Less(universe.PlayerCredits, 10000.0, "通牒金额被扣除");
            Assert.AreEqual(0, redBanner.UltimatumsRejected, "支付重置拒绝计数 (缓和路径)");
            Assert.AreEqual(FactionStance.Tense, redBanner.Stance, "支付维持紧张而非战争");
        }

        [Test]
        public void MerchantColdShoulder_MinusOnePerDay_AfterFiveQuietDays()
        {
            var universe = TestUtil.NewUniverse(212UL, 96);
            universe.FactionLayerVisible = true;
            var merchant = universe.FactionsSandbox.Get(FactionSystem.MerchantId);
            int before = merchant.AttitudeToPlayer;
            for (long i = 0; i < 9L * GameConstants.TicksPerDay; i++)
            {
                universe.Step();
            }
            // 9 days without a trade → at least (9-5)=4 days of -1.
            Assert.LessOrEqual(merchant.AttitudeToPlayer, before - 3,
                "连续 5 游戏日无交易后 attitude -1/日 (docs/plan/07 商盟行为 2)");
        }

        [Test]
        public void RouteInsurance_RefundsCargoLoss()
        {
            var universe = TestUtil.NewUniverse(213UL, 96);
            universe.ActiveWorld.Tech.UnlockBranch("branch_orbital_logistics");
            Assert.IsTrue(universe.ActiveWorld.Tech.IsUnlocked("branch_orbital_logistics_5"), "保险节点在链上");
            double before = universe.PlayerCredits;
            // No landing beacon → 5% loss on 40 water; insurance refunds it in credits.
            universe.QueueCargoTransit(1, "dustloam",
                new List<Ingredient> { new Ingredient { ItemId = ItemIds.Water, Count = 40 } });
            for (int i = 0; i < GameConstants.TicksPerHour * 2; i++)
            {
                universe.Step();
            }
            double expectedRefund = universe.PriceOf(ItemIds.Water) * 2;
            Assert.AreEqual(before + expectedRefund, universe.PlayerCredits, 0.01,
                "航线保险全额补偿货损 (branch_orbital_logistics_5)");
        }

        [Test]
        public void FactionParams_LoadFromCsv_MatchDefaults()
        {
            var system = new FactionSystem();
            system.InitFromCsv(File.ReadAllLines(Path.Combine(TestUtil.FindDataDir(), "faction_params.csv")));
            Assert.AreEqual(3, system.Factions.Count);
            var redBanner = system.Get(FactionSystem.RedBannerId);
            Assert.AreEqual(1.3f, redBanner.GrowthMult, 0.001f, "参数来自数据表 (M6-T2)");
            Assert.AreEqual(2.5f, system.Get(FactionSystem.SilentId).DefenseMult, 0.001f);
            Assert.AreEqual("warmmarsh", system.Get(FactionSystem.MerchantId).HeldBodies[0]);
        }
    }
}
