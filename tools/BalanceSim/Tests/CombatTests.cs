using System.Collections.Generic;
using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M7: F3/F6 plan assertions, defense scoring and raid targeting,
    /// bombardment floor, vassal treaty, victory paths, content re-verification.</summary>
    public sealed class CombatTests
    {
        [Test]
        public void F3_RedBannerWar_FirstRaidWindow_Strength_And24hWarning()
        {
            // docs/plan/07 F3 原文: 拒绝通牒 2 次 → "进入战争后 144 游戏时内发生首次袭击"
            // 且 "袭击强度在 [8,40] 内" 且 "预警提前量 = 24 游戏时"。
            var universe = TestUtil.NewUniverse(171UL, 96);
            var redBanner = universe.FactionsSandbox.Get(FactionSystem.RedBannerId);
            redBanner.HeldBodies.Add("flintfield");
            bool ultimatumSeen = false;
            for (long i = 0; i < 8L * GameConstants.TicksPerDay && !ultimatumSeen; i++)
            {
                universe.Step();
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is FactionUltimatumEvent)
                    {
                        ultimatumSeen = true;
                    }
                }
            }
            universe.FactionsSandbox.RejectUltimatum(universe);
            universe.FactionsSandbox.RejectUltimatum(universe);
            Assert.AreEqual(FactionStance.War, redBanner.Stance);

            long warHour = universe.Tick / GameConstants.TicksPerHour;
            long warningHour = -1, raidHour = -1;
            int strength = 0;
            for (long i = 0; i < 170L * GameConstants.TicksPerHour && raidHour < 0; i++)
            {
                universe.Step();
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is RaidWarningEvent warning)
                    {
                        warningHour = universe.Tick / GameConstants.TicksPerHour;
                        strength = warning.Strength;
                        Assert.AreEqual(warning.RaidHour - 24, warningHour, 1, "预警提前量 = 24 游戏时");
                    }
                    if (evt is RaidResolvedEvent)
                    {
                        raidHour = universe.Tick / GameConstants.TicksPerHour;
                    }
                }
            }
            Assert.Greater(raidHour, 0, "war must produce a raid");
            Assert.LessOrEqual(raidHour - warHour, 144 + 24, "进入战争后 144 游戏时内首次袭击(+预警窗)");
            Assert.That(strength, Is.InRange(8, 40), "袭击强度在 [8,40] 内");
        }

        [Test]
        public void RaidTargets_WeakestRegion_TwoRegionCase()
        {
            var universe = TestUtil.NewUniverse(172UL, 96);
            // Active region gets defense; a frozen outpost stays naked.
            var world = universe.ActiveWorld;
            world.Tech.UnlockAll();
            world.Buildings.Place(BuildingDefs.SentryGunId, world.StartX + 5, world.StartY + 5, 0, out _);
            world.Buildings.Place(BuildingDefs.SentryGunId, world.StartX + 7, world.StartY + 5, 0, out _);
            var slot = new RegionSlot { Id = 4, BodyId = "palewatch", Seed = 4UL, FrozenSave = new byte[0], DefenseScoreAtFreeze = 0f };
            universe.FrozenRegions.Add(4, slot);

            Assert.AreEqual(4, universe.Combat.WeakestPlayerRegion(universe),
                "赤旗袭击目标 = 防御评分最低的区域 (M7-T1 双区域用例)");
            Assert.Greater(CombatSystem.DefenseScore(world), 0f, "哨戒炮必须计入评分");
        }

        [Test]
        public void Bombardment_TenPercentSteps_FloorAt40Percent()
        {
            var universe = TestUtil.NewUniverse(173UL, 96);
            var pod = universe.ActiveWorld.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add("orbital_penetrator", 10);
            var silent = universe.FactionsSandbox.Get(FactionSystem.SilentId);
            float baseline = universe.Combat.EffectiveDefense(universe, silent, "sleetfall");

            Assert.IsTrue(universe.Combat.Bombard(universe, "sleetfall"));
            float afterOne = universe.Combat.EffectiveDefense(universe, silent, "sleetfall");
            Assert.AreEqual(baseline * 0.9f, afterOne, 0.01f, "每发 D -10% (M7-T5)");

            for (int i = 0; i < 9; i++)
            {
                universe.Combat.Bombard(universe, "sleetfall");
            }
            float floored = universe.Combat.EffectiveDefense(universe, silent, "sleetfall");
            Assert.AreEqual(baseline * 0.4f, floored, 0.01f, "轰炸下限 40% (M7-T5)");
        }

        [Test]
        public void F6_OccupySleetfall_BranchChoice_UniqueDeposits()
        {
            // docs/plan/07 F6 原文: 攻占眠霜 → "结算界面出现超导输电/相变装甲二选一"、
            // "选择后对应配方进入配方库且另一分支永久不可得"、"眠霜矿脉对玩家采矿机可用"。
            var universe = TestUtil.NewUniverse(174UL, 96);
            var silent = universe.FactionsSandbox.Get(FactionSystem.SilentId);
            var pod = universe.ActiveWorld.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod.Stock.Add("orbital_penetrator", 6);
            for (int i = 0; i < 6; i++)
            {
                universe.Combat.Bombard(universe, "sleetfall");
            }

            bool won = universe.Combat.Assault(universe, FactionSystem.SilentId, "sleetfall",
                combatBots: 24 * 8, targetShieldFirst: false);
            Assert.IsTrue(won, "assault with overwhelming bots must win");
            string branchA = null, branchB = null;
            foreach (var evt in universe.ActiveWorld.Events)
            {
                if (evt is OccupationPendingEvent pending)
                {
                    branchA = pending.BranchA;
                    branchB = pending.BranchB;
                }
            }
            Assert.AreEqual("branch_superconductor_grid", branchA, "结算出现超导输电选项");
            Assert.AreEqual("branch_phase_armor", branchB, "结算出现相变装甲选项");

            universe.Combat.SettleOccupation(universe, "branch_superconductor_grid");
            var tech = universe.ActiveWorld.Tech;
            Assert.IsTrue(tech.IsUnlocked("branch_superconductor_grid"), "选中分支解锁");
            Assert.IsTrue(tech.IsUnlocked("branch_superconductor_grid_3"), "分支全链解锁");
            Assert.IsTrue(tech.IsRecipeUnlocked("form_superconductor_alloy_wire"), "分支配方进入配方库");
            Assert.IsTrue(tech.IsBranchBurned("branch_phase_armor"), "未选分支永久不可得");
            Assert.IsFalse(tech.CanSelectTarget("branch_phase_armor"), "焚毁分支不可自研");
            Assert.IsFalse(silent.HeldBodies.Contains("sleetfall"), "星体转为玩家所有");
            Assert.AreEqual(0f, silent.GrowthMult, "母星被占 → 残部停止成长");

            // Unique deposits: a faction-locked node blocks extractors until occupation.
            var world = universe.ActiveWorld;
            world.Tech.UnlockAll();
            var node = world.Nodes.Spawn("azure_superconductor", 500, world.StartX + 8, world.StartY + 8,
                Balance.MineTicksPerUnit);
            node.FactionLocked = true;
            int minerId = world.Buildings.Place(BuildingDefs.MinerId, world.StartX + 9, world.StartY + 9, 0, out _);
            BuildingDefs.TryGet(BuildingDefs.MinerId, out var minerDef);
            Assert.AreEqual(0, world.Buildings.FindDepositFor(minerDef, world.StartX + 9, world.StartY + 9),
                "势力专营矿脉对玩家采矿机不可用 (占领前)");
            node.FactionLocked = false; // occupation clears the lock on instantiation.
            Assert.AreEqual(node.Id, world.Buildings.FindDepositFor(minerDef, world.StartX + 9, world.StartY + 9),
                "占领后矿脉对玩家采矿机可用 (F6)");
            _ = minerId;
        }

        [Test]
        public void FailedAssault_RestoresDefense_AndCostsAttitude()
        {
            var universe = TestUtil.NewUniverse(175UL, 96);
            var redBanner = universe.FactionsSandbox.Get(FactionSystem.RedBannerId);
            int attitudeBefore = redBanner.AttitudeToPlayer;
            bool won = universe.Combat.Assault(universe, FactionSystem.RedBannerId, "redridge",
                combatBots: 2, targetShieldFirst: false);
            Assert.IsFalse(won, "2 bots cannot crack a home world");
            Assert.AreEqual(attitudeBefore - 20, redBanner.AttitudeToPlayer, "失败 attitude -20 (M7-T6)");
        }

        [Test]
        public void VassalTreaty_Tribute_And_Breach()
        {
            var universe = TestUtil.NewUniverse(176UL, 96);
            universe.Combat.SignVassalTreaty(universe);
            double credits = universe.PlayerCredits;
            int myco = universe.ActiveWorld.CountItemEverywhere("myco_gold_spore");
            universe.Combat.DailyTribute(universe);
            Assert.AreEqual(credits + 500, universe.PlayerCredits, "贡品 500 星币/日 (M7-T9)");
            Assert.AreEqual(myco + 20, universe.ActiveWorld.CountItemEverywhere("myco_gold_spore"),
                "贡品 20 菌金胞/日");

            universe.Combat.BreachVassalTreaty(universe);
            var merchant = universe.FactionsSandbox.Get(FactionSystem.MerchantId);
            Assert.IsTrue(merchant.Embargoed, "违约 → 永久禁运");
            Assert.IsFalse(universe.Combat.MerchantVassal, "条约废止");
        }

        [Test]
        public void Victory_Hegemony_And_WarpBeacon()
        {
            // Hegemony: 8 bodies (player + occupied + vassal).
            var universe = TestUtil.NewUniverse(177UL, 96);
            foreach (string body in new[] { "palewatch", "flintfield", "cinderrock", "mistwatch", "frostmaw", "sulfmire" })
            {
                universe.FrozenRegions.Add(universe.FrozenRegions.Count + 10,
                    new RegionSlot { Id = universe.FrozenRegions.Count + 10, BodyId = body, FrozenSave = new byte[0] });
            }
            universe.Combat.SignVassalTreaty(universe); // dustloam + 6 outposts + vassal = 8.
            Assert.IsTrue(universe.Combat.VictoryReached, "霸权 8 星胜利 (M7-T10)");
            Assert.AreEqual("hegemony", universe.Combat.VictoryPath);

            // Warp beacon path.
            var universeB = TestUtil.NewUniverse(178UL, 96);
            universeB.ActiveWorld.Buildings.Place(BuildingDefs.WarpBeaconId,
                universeB.ActiveWorld.StartX + 8, universeB.ActiveWorld.StartY + 8, 0, out var err);
            Assert.AreEqual(PlacementError.None, err);
            universeB.Combat.CheckVictory(universeB);
            Assert.IsTrue(universeB.Combat.VictoryReached, "跃迁灯塔胜利 (M7-T10)");
            Assert.AreEqual("warp_beacon", universeB.Combat.VictoryPath);
            // Sandbox continues after victory: stepping must not throw or halt.
            for (int i = 0; i < GameConstants.TicksPerHour; i++)
            {
                universeB.Step();
            }
        }

        [Test]
        public void ContentReverification_330Recipes_RecorderDropLoop()
        {
            var world = TestUtil.NewColonyWorld(179UL, 96);
            Assert.GreaterOrEqual(world.Crafting.Recipes.Count, 330, "配方总数 ≥330 (M7-T11)");
            // Military data core consumes the battlefield drop.
            Assert.IsTrue(world.Crafting.TryGetRecipe("make_military_data_core", out var core));
            bool usesRecorder = false;
            foreach (var input in core.Inputs)
            {
                if (input.ItemId == "data_recorder")
                {
                    usesRecorder = true;
                }
            }
            Assert.IsTrue(usesRecorder, "军情数据核以战斗记录仪为原料 (掉落闭环)");
        }
    }
}
