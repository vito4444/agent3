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
        public void AutoQuotes_KeepBoardAlive_WhenMerchantIsRemnant()
        {
            var universe = TestUtil.NewUniverse(214UL, 96);
            universe.FactionLayerVisible = true;
            var merchant = universe.FactionsSandbox.Get(FactionSystem.MerchantId);
            merchant.Embargoed = true; // 残部/禁运态: 常规报价停摆
            for (long i = 0; i < 13L * GameConstants.TicksPerHour; i++)
            {
                universe.Step();
            }
            Assert.AreEqual(0, universe.FactionsSandbox.Quotes.Count, "无自动报价科技时禁运停板");

            universe.ActiveWorld.Tech.UnlockBranch("branch_orbital_logistics");
            for (long i = 0; i < 12L * GameConstants.TicksPerHour; i++)
            {
                universe.Step();
            }
            Assert.GreaterOrEqual(universe.FactionsSandbox.Quotes.Count, 6,
                "自动报价单: 中立保税区顶上, 板子照刷 (branch_orbital_logistics_2)");

            // Deals settle but never move the embargoed merchant's attitude/treasury.
            int attitudeBefore = merchant.AttitudeToPlayer;
            float treasuryBefore = merchant.Treasury;
            universe.PlayerCredits = 100000;
            MerchantQuote buyable = null;
            foreach (var quote in universe.FactionsSandbox.Quotes)
            {
                if (quote.MerchantSells)
                {
                    buyable = quote;
                }
            }
            Assert.IsNotNull(buyable, "板上有卖单");
            Assert.IsTrue(universe.FactionsSandbox.AcceptQuote(universe, buyable.Id), "中立区报价可成交");
            for (long i = 0; i < 7L * GameConstants.TicksPerHour; i++)
            {
                universe.Step();
            }
            Assert.AreEqual(attitudeBefore, merchant.AttitudeToPlayer, "中立区成交不改商盟态度");
            Assert.AreEqual(treasuryBefore, merchant.Treasury, 0.001f, "中立区成交不进商盟金库");
        }

        [Test]
        public void MarketRadar_PreviewsNextThreeBoards_Deterministically()
        {
            var universe = TestUtil.NewUniverse(215UL, 96);
            universe.FactionLayerVisible = true;
            Assert.AreEqual(0, universe.FactionsSandbox.PeekUpcomingQuotes(universe, 3).Count,
                "未解锁行情雷达时无预告");

            universe.ActiveWorld.Tech.UnlockBranch("branch_orbital_logistics");
            var preview = universe.FactionsSandbox.PeekUpcomingQuotes(universe, 3);
            Assert.AreEqual(18, preview.Count, "3 张预告板 × 每板 6 条 (态度<60 无稀有位)");

            // Advance past the current board's expiry; the live board must match the
            // first previewed board item-for-item.
            var firstBoard = new List<MerchantQuote>();
            foreach (var quote in preview)
            {
                if (quote.ExpiresHour == preview[0].ExpiresHour)
                {
                    firstBoard.Add(quote);
                }
            }
            for (long i = 0; i < 13L * GameConstants.TicksPerHour; i++)
            {
                universe.Step();
            }
            var live = universe.FactionsSandbox.Quotes;
            Assert.AreEqual(firstBoard.Count, live.Count, "到期后的实刷板条数与预告一致");
            for (int i = 0; i < firstBoard.Count; i++)
            {
                Assert.AreEqual(firstBoard[i].ItemId, live[i].ItemId, "预告条目 " + i + " 品类一致");
                Assert.AreEqual(firstBoard[i].Count, live[i].Count, "预告条目 " + i + " 数量一致");
                Assert.AreEqual(firstBoard[i].UnitPrice, live[i].UnitPrice, 0.001, "预告条目 " + i + " 单价一致");
            }
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
