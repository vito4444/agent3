using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    public sealed class RaidWarningEvent : ISimEvent
    {
        public int TargetRegionId;
        public int Strength;
        public int Waves;
        public long RaidHour;
    }

    public sealed class RaidResolvedEvent : ISimEvent
    {
        public int TargetRegionId;
        public bool Repelled;
        public int RecordersDropped;
    }

    public sealed class RegionLostEvent : ISimEvent
    {
        public int RegionId;
        public int SurvivorsMovedTo;
        public int Survivors;
    }

    public sealed class BombardmentEvent : ISimEvent
    {
        public string BodyId;
        public float DefenseAfterPercent;
    }

    public sealed class AssaultResolvedEvent : ISimEvent
    {
        public string BodyId;
        public bool Victory;
        public bool ShieldWasDownFirst;
    }

    public sealed class OccupationPendingEvent : ISimEvent
    {
        public string FactionId;
        public string BodyId;
        public string BranchA;
        public string BranchB;
    }

    public sealed class OccupationSettledEvent : ISimEvent
    {
        public string BodyId;
        public string ChosenBranch;
        public string BurnedBranch;
    }

    public sealed class VassalTreatyEvent : ISimEvent
    {
        public bool Signed;
    }

    public sealed class VictoryEvent : ISimEvent
    {
        public string Path;
    }

    /// <summary>
    /// Combat and occupation (docs/plan/07, sandbox resolution layer): defense scoring,
    /// red-banner raids with 24h warnings, orbital bombardment, assault landings,
    /// occupation settlement (body + unique deposits + branch pick), the merchant vassal
    /// treaty, and both victory paths. Map-level unit combat is presented at L1 but the
    /// outcome math lives here.
    /// </summary>
    public sealed class CombatSystem
    {
        private const float SentryScore = 6f;
        private const float LaserScore = 9f;
        private const float WallScore = 0.5f;
        private const float StationedBotScore = 2f;
        private const float ShieldScore = 10f;
        private const float RaidStrengthFactor = 0.05f;
        private const int RaidStrengthMin = 8;
        private const int RaidStrengthMax = 40;
        private const int RaidIntervalMinHours = 72;
        private const int RaidIntervalMaxHours = 144;
        private const int RaidWarningHours = 24;
        private const float BombardReductionStep = 0.10f;
        private const float BombardFloor = 0.40f;
        private const float AssaultBotPower = 2.2f;
        /// <summary>自爆蛛: one-shot charge, 3× a combat bot (branch_swarm_tactics_2).</summary>
        private const float BreacherBotPower = 6.6f;
        /// <summary>Half of a body's defense is turret-based; jammers cut that targeting by 50%.</summary>
        private const float TurretShareOfDefense = 0.5f;
        private const float JammerTargetingCut = 0.5f;
        private const float DefensePowerShare = 0.6f;
        private const int RecorderDropDivisor = 4;
        private const int HegemonyBodies = 8;
        private const int VassalTributeMyco = 20;
        private const int VassalTributeCredits = 500;
        private const int BotsPerWave = 16;
        private const float PercentScale = 100f;
        private const int AssaultFailAttitude = 20;

        /// <summary>Pending raid state (persisted in the universe save).</summary>
        public long NextRaidHour;
        public long WarnedRaidHour;
        public int WarnedStrength;
        public int WarnedRegionId;
        /// <summary>bodyId → bombardment reduction fraction accumulated (≤ 1 - floor).</summary>
        public Dictionary<string, float> BombardReduction = new Dictionary<string, float>();
        /// <summary>Pending occupation choice after a won assault.</summary>
        public string PendingOccupationFaction = string.Empty;
        public string PendingOccupationBody = string.Empty;
        public bool MerchantVassal;
        public bool VictoryReached;
        public string VictoryPath = string.Empty;

        // ---------------------------------------------------------------- defense score

        /// <summary>防御评分 = 炮塔 DPS 折算 + 墙体系数 + 驻守战斗蛛 (docs/plan/07).</summary>
        public static float DefenseScore(World world)
        {
            float score = 0f;
            foreach (var pair in world.Buildings.All)
            {
                switch (pair.Value.DefId)
                {
                    case BuildingDefs.SentryGunId: score += SentryScore; break;
                    case BuildingDefs.LaserTowerId: score += LaserScore; break;
                    case BuildingDefs.WallId: score += WallScore; break;
                    case BuildingDefs.ShieldDomeId: score += ShieldScore; break;
                }
            }
            score += world.CountItemEverywhere("combat_bot") * StationedBotScore;
            return score;
        }



        // ---------------------------------------------------------------- raids

        public void HourlyTick(Universe universe, long hour)
        {
            var redBanner = universe.FactionsSandbox.Get(FactionSystem.RedBannerId);
            if (redBanner == null || redBanner.Stance != FactionStance.War)
            {
                return;
            }
            if (NextRaidHour == 0)
            {
                ScheduleNextRaid(universe, Math.Max(hour, redBanner.RaidsBeginHour));
            }
            if (WarnedRaidHour == 0 && hour >= NextRaidHour - RaidWarningHours)
            {
                WarnedRaidHour = NextRaidHour;
                WarnedStrength = (int)Math.Clamp(redBanner.P * RaidStrengthFactor, RaidStrengthMin, RaidStrengthMax);
                WarnedRegionId = WeakestPlayerRegion(universe);
                universe.ActiveWorld.Events.Add(new RaidWarningEvent
                {
                    TargetRegionId = WarnedRegionId,
                    Strength = WarnedStrength,
                    Waves = 1 + WarnedStrength / BotsPerWave,
                    RaidHour = NextRaidHour
                });
            }
            if (WarnedRaidHour != 0 && hour >= WarnedRaidHour)
            {
                ResolveRaid(universe, WarnedRegionId, WarnedStrength);
                WarnedRaidHour = 0;
                ScheduleNextRaid(universe, hour);
            }
        }

        private void ScheduleNextRaid(Universe universe, long fromHour)
        {
            var stream = universe.ActiveWorld.GetStream("raids");
            NextRaidHour = fromHour + stream.NextInt(RaidIntervalMinHours, RaidIntervalMaxHours + 1);
        }

        /// <summary>Raid target = the player's weakest region by defense score (M7-T1).</summary>
        public int WeakestPlayerRegion(Universe universe)
        {
            int best = universe.ActiveRegionId;
            float bestScore = DefenseScore(universe.ActiveWorld);
            foreach (var pair in universe.FrozenRegions)
            {
                float score = pair.Value.DefenseScoreAtFreeze;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = pair.Key;
                }
            }
            return best;
        }

        private void ResolveRaid(Universe universe, int regionId, int strength)
        {
            // The active region fights it out entity by entity (docs/plan/07 防御战);
            // the outcome arrives via WaveRepelled / CommandCoreDestroyed events.
            if (regionId == universe.ActiveRegionId)
            {
                int waves = 1 + strength / BotsPerWave;
                universe.ActiveWorld.Battle.QueueRaid(universe.ActiveWorld, strength, waves);
                universe.ActiveWorld.Events.Add(new RaidResolvedEvent
                {
                    TargetRegionId = regionId,
                    Repelled = false,
                    RecordersDropped = 0
                });
                return;
            }

            // Frozen regions resolve numerically against their frozen defense score.
            float defense = universe.FrozenRegions.TryGetValue(regionId, out var slot)
                ? slot.DefenseScoreAtFreeze : 0f;
            bool repelled = defense >= strength;
            if (repelled)
            {
                int recorders = Math.Max(1, strength / RecorderDropDivisor);
                if (universe.FrozenRegions.TryGetValue(regionId, out var frozen))
                {
                    frozen.Stockpile.TryGetValue("data_recorder", out int existing);
                    frozen.Stockpile["data_recorder"] = existing + recorders;
                }
                universe.ActiveWorld.Events.Add(new RaidResolvedEvent
                {
                    TargetRegionId = regionId,
                    Repelled = true,
                    RecordersDropped = recorders
                });
                return;
            }

            universe.LoseRegion(regionId);
            universe.ActiveWorld.Events.Add(new RaidResolvedEvent
            {
                TargetRegionId = regionId,
                Repelled = false,
                RecordersDropped = 0
            });
        }

        // ---------------------------------------------------------------- offense

        /// <summary>Effective faction defense on a body: D = P × share × personality, minus
        /// bombardment (floor 40%, docs/plan/07).</summary>
        public float EffectiveDefense(Universe universe, Faction faction, string bodyId)
        {
            float share = bodyId == faction.HeldBodies[0] ? DefensePowerShare
                : (1f - DefensePowerShare) / Math.Max(1, faction.HeldBodies.Count - 1);
            float baseD = faction.P * share * faction.DefenseMult;
            BombardReduction.TryGetValue(bodyId, out float reduction);
            return baseD * (1f - Math.Min(reduction, 1f - BombardFloor));
        }

        /// <summary>Orbital bombardment (M7-T5): each penetrator −10% D, floor 40%.</summary>
        public bool Bombard(Universe universe, string bodyId)
        {
            if (!universe.ActiveWorld.TryConsumeItemAnywhere("orbital_penetrator"))
            {
                return false;
            }
            BombardReduction.TryGetValue(bodyId, out float reduction);
            reduction = Math.Min(reduction + BombardReductionStep, 1f - BombardFloor);
            BombardReduction[bodyId] = reduction;
            universe.ActiveWorld.Events.Add(new BombardmentEvent
            {
                BodyId = bodyId,
                DefenseAfterPercent = (1f - reduction) * PercentScale
            });
            return true;
        }

        /// <summary>Assault landing (M7-T6): 24 combat bots vs the body's effective D.
        /// targetShieldFirst models the vassal precondition on the merchant home.
        /// 集群战术 units in the sandbox model (docs/plan/07 分支):
        /// 自爆蛛 breacherBots — one-shot charges, each adds BreacherBotPower attack;
        /// 干扰无人机 jammerDrones — any present halve the turret half of enemy defense
        /// (net ×0.75, "索敌 -50%"); 前线装配巢 forwardNest — a failed assault keeps
        /// siege pressure: enemy defense does not recover its 10%.</summary>
        public bool Assault(Universe universe, string factionId, string bodyId,
            int combatBots, bool targetShieldFirst,
            int breacherBots = 0, int jammerDrones = 0, bool forwardNest = false)
        {
            var faction = universe.FactionsSandbox.Get(factionId);
            if (faction == null || !faction.HeldBodies.Contains(bodyId))
            {
                return false;
            }
            float attack = combatBots * AssaultBotPower + breacherBots * BreacherBotPower;
            float defense = EffectiveDefense(universe, faction, bodyId);
            if (jammerDrones > 0)
            {
                defense *= 1f - TurretShareOfDefense * JammerTargetingCut;
            }
            bool victory = attack > defense;
            universe.ActiveWorld.Events.Add(new AssaultResolvedEvent
            {
                BodyId = bodyId,
                Victory = victory,
                ShieldWasDownFirst = targetShieldFirst
            });
            if (!victory)
            {
                // Failure: defense recovers 10%, attitude −20 (docs/plan/07 进攻战 3).
                if (!forwardNest)
                {
                    BombardReduction.TryGetValue(bodyId, out float reduction);
                    BombardReduction[bodyId] = Math.Max(0f, reduction - BombardReductionStep);
                }
                faction.AttitudeToPlayer -= AssaultFailAttitude;
                return false;
            }

            if (factionId == FactionSystem.MerchantId && targetShieldFirst)
            {
                // Vassal window instead of occupation (docs/plan/07 附庸).
                universe.ActiveWorld.Events.Add(new VassalTreatyEvent { Signed = false });
            }
            PendingOccupationFaction = factionId;
            PendingOccupationBody = bodyId;
            var branches = BranchesOf(factionId);
            universe.ActiveWorld.Events.Add(new OccupationPendingEvent
            {
                FactionId = factionId,
                BodyId = bodyId,
                BranchA = branches.a,
                BranchB = branches.b
            });
            return true;
        }

        public static (string a, string b) BranchesOf(string factionId)
        {
            switch (factionId)
            {
                case FactionSystem.SilentId: return ("branch_superconductor_grid", "branch_phase_armor");
                case FactionSystem.MerchantId: return ("branch_bio_refining", "branch_orbital_logistics");
                default: return ("branch_fast_reactor", "branch_swarm_tactics");
            }
        }

        /// <summary>Occupation settlement (M7-T7): body transfers, unique deposits unlock,
        /// one branch unlocks and the other burns forever (F6).</summary>
        public void SettleOccupation(Universe universe, string chosenBranch)
        {
            if (PendingOccupationFaction.Length == 0)
            {
                return;
            }
            var faction = universe.FactionsSandbox.Get(PendingOccupationFaction);
            var branches = BranchesOf(PendingOccupationFaction);
            string burned = chosenBranch == branches.a ? branches.b : branches.a;

            faction.HeldBodies.Remove(PendingOccupationBody);
            if (faction.HeldBodies.Count == 0)
            {
                faction.Stance = FactionStance.War;
                faction.Embargoed = true;
                faction.GrowthMult = 0f; // 残部: sandbox growth stops (docs/plan/07).
            }
            universe.OccupiedBodies.Add(PendingOccupationBody);
            universe.ActiveWorld.Tech.UnlockBranch(chosenBranch);
            universe.ActiveWorld.Tech.BurnBranch(burned);
            // Unique deposits open up when the region instantiates (FactionLocked clears).
            universe.ActiveWorld.Events.Add(new OccupationSettledEvent
            {
                BodyId = PendingOccupationBody,
                ChosenBranch = chosenBranch,
                BurnedBranch = burned
            });
            PendingOccupationFaction = string.Empty;
            PendingOccupationBody = string.Empty;
            CheckVictory(universe);
        }

        /// <summary>The merchant vassal treaty (M7-T9): daily tribute, permanent quotes,
        /// breach = void + permanent embargo. Counts toward hegemony.</summary>
        public void SignVassalTreaty(Universe universe)
        {
            MerchantVassal = true;
            PendingOccupationFaction = string.Empty;
            PendingOccupationBody = string.Empty;
            universe.ActiveWorld.Events.Add(new VassalTreatyEvent { Signed = true });
            CheckVictory(universe);
        }

        public void DailyTribute(Universe universe)
        {
            if (!MerchantVassal)
            {
                return;
            }
            var pod = universe.ActiveWorld.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            pod?.Stock.Add("myco_gold_spore", VassalTributeMyco);
            universe.PlayerCredits += VassalTributeCredits;
        }

        public void BreachVassalTreaty(Universe universe)
        {
            if (!MerchantVassal)
            {
                return;
            }
            MerchantVassal = false;
            var merchant = universe.FactionsSandbox.Get(FactionSystem.MerchantId);
            merchant.Embargoed = true;
            merchant.Stance = FactionStance.War;
            universe.ActiveWorld.Events.Add(new VassalTreatyEvent { Signed = false });
        }

        // ---------------------------------------------------------------- victory

        public void CheckVictory(Universe universe)
        {
            if (VictoryReached)
            {
                return;
            }
            int bodies = universe.PlayerBodies().Count + universe.OccupiedBodies.Count;
            foreach (string body in universe.PlayerBodies())
            {
                if (universe.OccupiedBodies.Contains(body))
                {
                    bodies--;
                }
            }
            if (MerchantVassal)
            {
                bodies++;
            }
            if (bodies >= HegemonyBodies)
            {
                VictoryReached = true;
                VictoryPath = "hegemony";
                universe.ActiveWorld.Events.Add(new VictoryEvent { Path = VictoryPath });
                return;
            }
            foreach (var pair in universe.ActiveWorld.Buildings.All)
            {
                if (pair.Value.DefId == "warp_beacon")
                {
                    VictoryReached = true;
                    VictoryPath = "warp_beacon";
                    universe.ActiveWorld.Events.Add(new VictoryEvent { Path = VictoryPath });
                    return;
                }
            }
        }
    }
}
