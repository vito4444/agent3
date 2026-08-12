using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    public enum JobType
    {
        Miner,
        Hauler,
        Builder,
        Operator,
        Farmer,
        Cook,
        Medic,
        Researcher
    }

    /// <summary>
    /// Job quotas and the job × task-type priority matrix (docs/plan/03: P0 forbidden to
    /// P4 highest; the default template works out of the box). Dispatch prefers workers
    /// whose job rates the task type higher, then distance.
    /// </summary>
    public sealed class JobSystem
    {
        public const int JobCount = 8;
        public const int TaskTypeCount = 10;
        public const int Forbidden = 0;

        /// <summary>Desired headcount per job; colonists are reassigned in id order.</summary>
        public readonly Dictionary<JobType, int> Quotas = new Dictionary<JobType, int>();

        /// <summary>matrix[job, taskType] ∈ 0..4.</summary>
        public readonly int[,] Matrix = new int[JobCount, TaskTypeCount];

        public JobSystem()
        {
            ApplyDefaultMatrix();
        }

        /// <summary>Default template (docs/plan/03): every job can pitch in, specialists lead.</summary>
        public void ApplyDefaultMatrix()
        {
            // Rows: Miner, Hauler, Builder, Operator, Farmer, Cook, Medic, Researcher.
            // Cols: Build, HaulToBlueprint, HaulToStation, HaulToStorage, Craft, Mine, Crank, Bury, Repair, Research.
            int[,] defaults =
            {
                { 2, 2, 2, 2, 1, 4, 2, 2, 2, 1 },
                { 2, 4, 4, 4, 1, 2, 2, 3, 2, 1 },
                { 4, 3, 2, 2, 1, 2, 1, 2, 4, 1 },
                { 2, 2, 3, 2, 4, 1, 4, 1, 3, 2 },
                { 2, 2, 2, 2, 3, 3, 1, 2, 1, 1 },
                { 1, 2, 3, 2, 4, 1, 1, 1, 1, 1 },
                { 1, 2, 2, 2, 2, 1, 1, 4, 3, 1 },
                { 1, 1, 1, 1, 2, 0, 1, 1, 1, 4 }
            };
            for (int j = 0; j < JobCount; j++)
            {
                for (int t = 0; t < TaskTypeCount; t++)
                {
                    Matrix[j, t] = defaults[j, t];
                }
            }
            foreach (JobType job in Enum.GetValues(typeof(JobType)))
            {
                if (!Quotas.ContainsKey(job))
                {
                    Quotas[job] = 0;
                }
            }
        }

        public int PriorityFor(JobType job, TaskType taskType)
        {
            int t = (int)taskType;
            if (t < 0 || t >= TaskTypeCount)
            {
                return 1;
            }
            return Matrix[(int)job, t];
        }

        public void SetMatrix(JobType job, TaskType taskType, int priority)
        {
            Matrix[(int)job, (int)taskType] = Math.Max(0, Math.Min(4, priority));
        }

        /// <summary>Fills quotas in colonist-id order; leftovers become Operators.</summary>
        public void ReassignJobs(World world)
        {
            var remaining = new Dictionary<JobType, int>(Quotas);
            foreach (var colonist in world.Colonists.AllSorted())
            {
                if (!colonist.Alive)
                {
                    continue;
                }
                bool assigned = false;
                foreach (JobType job in Enum.GetValues(typeof(JobType)))
                {
                    remaining.TryGetValue(job, out int want);
                    if (want > 0)
                    {
                        colonist.Job = job;
                        remaining[job] = want - 1;
                        assigned = true;
                        break;
                    }
                }
                if (!assigned)
                {
                    colonist.Job = JobType.Operator;
                }
            }
        }
    }

    /// <summary>
    /// Morale (docs/plan/02 table): base 60, food variety bonus, decaying event offset
    /// (deaths, night shifts), with the three effect bands — ≥70 work +10%, ≤30 −15%,
    /// ≤20 strike for the day.
    /// </summary>
    public sealed class MoraleSystem
    {
        public void OnColonistDied(World world)
        {
            foreach (var colonist in world.Colonists.AllSorted())
            {
                if (colonist.Alive)
                {
                    colonist.MoraleEventOffset += Balance.MoraleDeathPenalty;
                }
            }
        }

        /// <summary>Hourly upkeep: recompute morale; at day rollover settle night-shift
        /// penalties, food variety, event decay and strike checks.</summary>
        public void TickHourly(World world)
        {
            bool dayRollover = world.HourOfDay == 0;
            foreach (var colonist in world.Colonists.AllSorted())
            {
                if (!colonist.Alive)
                {
                    continue;
                }
                if (world.IsNight && colonist.Activity == ColonistActivity.WorkingTask)
                {
                    colonist.NightWorkHours++;
                }
                if (dayRollover)
                {
                    if (colonist.NightWorkHours >= 2)
                    {
                        colonist.MoraleEventOffset += Balance.MoraleNightShiftPenalty;
                    }
                    colonist.NightWorkHours = 0;
                    colonist.FoodVarietyYesterday = colonist.FoodsEatenToday.Count;
                    colonist.FoodsEatenToday.Clear();
                    colonist.MoraleEventOffset = MoveTowardZero(colonist.MoraleEventOffset, Balance.MoraleEventDecayPerDay);
                }

                float variety = colonist.FoodVarietyYesterday >= 3 ? Balance.MoraleFoodVariety3
                    : colonist.FoodVarietyYesterday >= 2 ? Balance.MoraleFoodVariety2
                    : 0f;
                colonist.Morale = Math.Max(0f, Math.Min(Balance.NeedMax,
                    Balance.MoraleStart + variety + colonist.MoraleEventOffset));

                if (dayRollover)
                {
                    bool strike = colonist.Morale <= Balance.MoraleStrikeThreshold;
                    if (strike && !colonist.OnStrike)
                    {
                        world.Events.Add(new ColonistStrikeEvent { ColonistId = colonist.Id });
                    }
                    colonist.OnStrike = strike;
                }
            }
        }

        public static float WorkSpeedFactor(Colonist colonist)
        {
            if (colonist.Morale >= Balance.MoraleHighThreshold)
            {
                return Balance.MoraleHighSpeedBonus;
            }
            if (colonist.Morale <= Balance.MoraleLowThreshold)
            {
                return Balance.MoraleLowSpeedMalus;
            }
            return 1f;
        }

        private static float MoveTowardZero(float value, float amount)
        {
            if (value > 0f)
            {
                return Math.Max(0f, value - amount);
            }
            return Math.Min(0f, value + amount);
        }
    }
}
