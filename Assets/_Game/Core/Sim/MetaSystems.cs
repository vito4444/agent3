using System.Collections.Generic;

namespace Starsoil.Core
{
    public enum AlertSeverity
    {
        Warning,
        Critical
    }

    /// <summary>Stable alert ids; UI maps them to localization keys alert_&lt;id&gt;.</summary>
    public static class AlertIds
    {
        public const string OxygenLow = "o2_low";
        public const string FoodLow = "food_low";
        public const string WaterLow = "water_low";
        public const string StormIncoming = "storm_incoming";
        public const string StormActive = "storm_active";
        public const string ColonistCritical = "colonist_critical_";
        public const string Defeat = "defeat";
    }

    public sealed class ActiveAlert
    {
        public string Id;
        public AlertSeverity Severity;
        public int X;
        public int Y;
    }

    /// <summary>Active alert registry (docs/plan/02: every hazard has an alert entry with
    /// a jump-to location; M1-T9). Raise/Clear emit events for the UI.</summary>
    public sealed class AlertSystem
    {
        private readonly Dictionary<string, ActiveAlert> _active = new Dictionary<string, ActiveAlert>();

        public IReadOnlyDictionary<string, ActiveAlert> Active => _active;

        public void Raise(World world, string id, AlertSeverity severity, int x, int y)
        {
            if (_active.ContainsKey(id))
            {
                return;
            }
            var alert = new ActiveAlert { Id = id, Severity = severity, X = x, Y = y };
            _active.Add(id, alert);
            world.Events.Add(new AlertRaisedEvent { AlertId = id, Severity = severity, X = x, Y = y });
        }

        public void Clear(World world, string id)
        {
            if (_active.Remove(id))
            {
                world.Events.Add(new AlertClearedEvent { AlertId = id });
            }
        }

        internal void RestoreFrom(List<SavedAlert> saved)
        {
            _active.Clear();
            foreach (var s in saved)
            {
                _active.Add(s.Id, new ActiveAlert
                {
                    Id = s.Id,
                    Severity = (AlertSeverity)s.Severity,
                    X = s.X,
                    Y = s.Y
                });
            }
        }
    }

    /// <summary>Run counters for tutorial predicates, defeat stats and achievements-to-be.</summary>
    public sealed class StatsSystem
    {
        public Dictionary<string, int> Mined = new Dictionary<string, int>();
        public Dictionary<string, int> Crafted = new Dictionary<string, int>();
        public Dictionary<string, int> Built = new Dictionary<string, int>();
        public Dictionary<string, int> Deaths = new Dictionary<string, int>();
        public int Burials;

        public void CountMined(string itemId, int count) => Bump(Mined, itemId, count);

        public void CountCrafted(string itemId, int count) => Bump(Crafted, itemId, count);

        public void CountBuilt(string defId) => Bump(Built, defId, 1);

        public void CountDeath(DeathCause cause) => Bump(Deaths, cause.ToString(), 1);

        public void CountBurial() => Burials++;

        public int MinedOf(string itemId) => Mined.TryGetValue(itemId, out int v) ? v : 0;

        public int CraftedOf(string itemId) => Crafted.TryGetValue(itemId, out int v) ? v : 0;

        public int BuiltOf(string defId) => Built.TryGetValue(defId, out int v) ? v : 0;

        private static void Bump(Dictionary<string, int> map, string key, int count)
        {
            map.TryGetValue(key, out int v);
            map[key] = v + count;
        }
    }

    /// <summary>
    /// First-act tutorial (M1-T10): ordered steps with world-state predicates. The UI
    /// renders tutorial_step_&lt;id&gt; localization keys and a highlight hint; steps can
    /// be skipped via settings (SetTutorialSkippedCommand).
    /// </summary>
    public sealed class TutorialSystem
    {
        public static readonly string[] StepIds =
        {
            "mine_ice",
            "melt_water",
            "gather_biomass",
            "craft_ration",
            "build_sleep_pod",
            "survive_night"
        };

        public int StepIndex;
        public bool Skipped;

        public bool Completed => Skipped || StepIndex >= StepIds.Length;

        public string CurrentStepId => Completed ? string.Empty : StepIds[StepIndex];

        public void Tick(World world)
        {
            if (Completed)
            {
                return;
            }
            bool satisfied;
            switch (CurrentStepId)
            {
                case "mine_ice":
                    satisfied = world.Stats.MinedOf(ItemIds.Ice) >= 2;
                    break;
                case "melt_water":
                    satisfied = world.Stats.CraftedOf(ItemIds.Water) >= 2;
                    break;
                case "gather_biomass":
                    satisfied = world.Stats.MinedOf(ItemIds.Biomass) >= 2;
                    break;
                case "craft_ration":
                    satisfied = world.Stats.CraftedOf(ItemIds.Ration) >= 1;
                    break;
                case "build_sleep_pod":
                    satisfied = world.Stats.BuiltOf(BuildingDefs.SleepPodId) >= 1;
                    break;
                case "survive_night":
                    satisfied = world.Day >= 1;
                    break;
                default:
                    satisfied = true;
                    break;
            }
            if (satisfied)
            {
                StepIndex++;
                world.Events.Add(new TutorialAdvancedEvent
                {
                    StepIndex = StepIndex,
                    NextStepId = CurrentStepId
                });
            }
        }
    }
}
