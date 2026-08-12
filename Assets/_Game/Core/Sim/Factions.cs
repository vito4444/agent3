using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    public enum FactionStance
    {
        Peace,
        Tense,
        War
    }

    public enum FactionPersonality
    {
        Merchant,
        Expansionist,
        Reclusive
    }

    public sealed class FactionExpandedEvent : ISimEvent
    {
        public string FactionId;
        public string BodyId;
    }

    public sealed class FactionUltimatumEvent : ISimEvent
    {
        public string FactionId;
        public int DemandCredits;
        public int Count;
    }

    public sealed class FactionWarDeclaredEvent : ISimEvent
    {
        public string FactionId;
        /// <summary>Raids begin this many ticks after the declaration (24h warning).</summary>
        public long RaidsBeginTick;
    }

    public sealed class Faction
    {
        public string Id = string.Empty;
        public string Zh = string.Empty;
        public string En = string.Empty;
        public FactionPersonality Personality;
        public float P;
        public float T = 1f;
        public float Treasury;
        public List<string> HeldBodies = new List<string>();
        public int AttitudeToPlayer;
        public FactionStance Stance = FactionStance.Peace;
        public float GrowthMult = 1f;
        public float TechMult = 1f;
        public float DefenseMult = 1f;
        public bool Embargoed;
        public int UltimatumsRejected;
        public long NextUltimatumHour;
        public long RaidsBeginHour;

        /// <summary>Instanced base tier (docs/plan/07): clamp(floor(T), 1, 4).</summary>
        public int BaseTier => Math.Max(1, Math.Min(4, (int)Math.Floor(T)));
    }

    /// <summary>
    /// AI faction sandbox (docs/plan/07): scalar power/tech growth per star-map tick
    /// (one game hour), expansion checks every 6 hours by personality rules over the
    /// 曦光 adjacency graph. Factions never fight each other and never take player or
    /// rival bodies; friction with the player follows the Red Banner script.
    /// </summary>
    public sealed class FactionSystem
    {
        public const string MerchantId = "merchant";
        public const string RedBannerId = "redbanner";
        public const string SilentId = "silent";

        private const float PowerGrowthBase = 1.2f;
        private const float TechGrowthBase = 0.001f;
        private const float HeldBodiesExponent = 0.7f;
        private const int ConquestCostPerHop = 400;
        private const int ExpansionCheckHours = 6;
        private const float StartPower = 300f;
        private const float UltimatumDemandFactor = 0.2f;
        private const int UltimatumIntervalDays = 3;
        private const int UltimatumsBeforeWar = 2;
        private const int WarWarningHours = 24;

        /// <summary>曦光 adjacency graph (docs/plan/02 邻接图).</summary>
        private static readonly Dictionary<string, string[]> Adjacency = new Dictionary<string, string[]>
        {
            { "cinderrock", new[] { "dustloam" } },
            { "dustloam", new[] { "cinderrock", "palewatch", "flintfield" } },
            { "palewatch", new[] { "dustloam" } },
            { "flintfield", new[] { "dustloam", "warmmarsh" } },
            { "warmmarsh", new[] { "flintfield", "mistwatch", "redridge" } },
            { "mistwatch", new[] { "warmmarsh" } },
            { "redridge", new[] { "warmmarsh", "azurecolossus" } },
            { "azurecolossus", new[] { "redridge", "frostmaw", "sulfmire", "ringfield", "sleetfall" } },
            { "frostmaw", new[] { "azurecolossus" } },
            { "sulfmire", new[] { "azurecolossus" } },
            { "ringfield", new[] { "azurecolossus" } },
            { "sleetfall", new[] { "azurecolossus" } }
        };

        public List<Faction> Factions { get; } = new List<Faction>();

        public void InitDefault()
        {
            Factions.Clear();
            Factions.Add(new Faction
            {
                Id = MerchantId, Zh = "星贸商盟", En = "Star Trade Compact",
                Personality = FactionPersonality.Merchant,
                P = StartPower, GrowthMult = 1.0f, TechMult = 1.0f, DefenseMult = 1.0f,
                HeldBodies = { "warmmarsh" },
                AttitudeToPlayer = 10
            });
            Factions.Add(new Faction
            {
                Id = RedBannerId, Zh = "赤旗军团", En = "Red Banner Legion",
                Personality = FactionPersonality.Expansionist,
                P = StartPower, GrowthMult = 1.3f, TechMult = 0.8f, DefenseMult = 1.2f,
                HeldBodies = { "redridge" },
                AttitudeToPlayer = -10
            });
            Factions.Add(new Faction
            {
                Id = SilentId, Zh = "静默会", En = "The Silent Accord",
                Personality = FactionPersonality.Reclusive,
                P = StartPower, GrowthMult = 1.0f, TechMult = 1.6f, DefenseMult = 2.5f,
                HeldBodies = { "sleetfall" },
                AttitudeToPlayer = 0
            });
        }

        public Faction Get(string id)
        {
            foreach (var faction in Factions)
            {
                if (faction.Id == id)
                {
                    return faction;
                }
            }
            return null;
        }

        /// <summary>One star-map tick (game hour). playerBodies = bodies with player regions.</summary>
        public void HourlyTick(Universe universe, HashSet<string> playerBodies)
        {
            HourlyTick(universe, playerBodies, universe.Tick / GameConstants.TicksPerHour);
        }

        /// <summary>Sandbox-clock variant (tests drive the hour directly).</summary>
        public void HourlyTick(Universe universe, HashSet<string> playerBodies, long hour)
        {
            foreach (var faction in Factions)
            {
                faction.P += PowerGrowthBase * faction.GrowthMult *
                             (float)Math.Pow(faction.HeldBodies.Count, HeldBodiesExponent);
                faction.T += TechGrowthBase * faction.TechMult;
            }
            if (hour % ExpansionCheckHours == 0)
            {
                foreach (var faction in Factions)
                {
                    TryExpand(universe, faction, playerBodies);
                }
            }
            TickRedBannerFriction(universe, playerBodies, hour);
        }

        private void TryExpand(Universe universe, Faction faction, HashSet<string> playerBodies)
        {
            if (faction.Personality == FactionPersonality.Reclusive)
            {
                return;
            }
            var neutral = NeutralBodies(universe, playerBodies);
            string target = null;
            int cost = int.MaxValue;
            if (faction.Personality == FactionPersonality.Merchant)
            {
                foreach (string candidate in new[] { "flintfield", "mistwatch" })
                {
                    if (neutral.Contains(candidate))
                    {
                        int c = ConquestCost(faction, candidate);
                        if (c < cost)
                        {
                            cost = c;
                            target = candidate;
                        }
                    }
                }
                if (target == null || faction.P <= cost * 2)
                {
                    return;
                }
            }
            else
            {
                // Red Banner: nearest neutral to the player's cradle; ties → cheaper;
                // still tied → higher orbit index (docs/plan/07 pacing: 苍卫 before 灼岩).
                int bestDistance = int.MaxValue;
                int bestOrbit = -1;
                foreach (string candidate in neutral)
                {
                    int distance = Hops("dustloam", candidate);
                    int c = ConquestCost(faction, candidate);
                    universe.Bodies.TryGet(candidate, out var body);
                    int orbit = body?.OrbitIndex ?? 0;
                    bool better = distance < bestDistance ||
                                  (distance == bestDistance && c < cost) ||
                                  (distance == bestDistance && c == cost && orbit > bestOrbit);
                    if (better)
                    {
                        bestDistance = distance;
                        cost = c;
                        bestOrbit = orbit;
                        target = candidate;
                    }
                }
                if (target == null || faction.P <= cost)
                {
                    return;
                }
            }
            faction.P -= cost;
            faction.HeldBodies.Add(target);
            universe.ActiveWorld.Events.Add(new FactionExpandedEvent { FactionId = faction.Id, BodyId = target });
        }

        private List<string> NeutralBodies(Universe universe, HashSet<string> playerBodies)
        {
            var held = new HashSet<string>();
            foreach (var faction in Factions)
            {
                foreach (string body in faction.HeldBodies)
                {
                    held.Add(body);
                }
            }
            var neutral = new List<string>();
            foreach (var pair in universe.Bodies.All)
            {
                if (pair.Value.Landable && !held.Contains(pair.Key) && !playerBodies.Contains(pair.Key))
                {
                    neutral.Add(pair.Key);
                }
            }
            neutral.Sort(StringComparer.Ordinal);
            return neutral;
        }

        public int ConquestCost(Faction faction, string targetBody)
        {
            int best = int.MaxValue;
            foreach (string held in faction.HeldBodies)
            {
                best = Math.Min(best, Hops(held, targetBody));
            }
            return ConquestCostPerHop * Math.Max(1, best);
        }

        /// <summary>BFS shortest path over the adjacency graph.</summary>
        public static int Hops(string from, string to)
        {
            if (from == to)
            {
                return 0;
            }
            var distance = new Dictionary<string, int> { [from] = 0 };
            var queue = new Queue<string>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!Adjacency.TryGetValue(current, out var neighbors))
                {
                    continue;
                }
                foreach (string next in neighbors)
                {
                    if (distance.ContainsKey(next))
                    {
                        continue;
                    }
                    distance[next] = distance[current] + 1;
                    if (next == to)
                    {
                        return distance[next];
                    }
                    queue.Enqueue(next);
                }
            }
            return int.MaxValue / ConquestCostPerHop;
        }

        // ---------------------------------------------------------------- red banner friction

        /// <summary>Red Banner friction chain (docs/plan/07 + M6-T8): adjacency → Tense,
        /// an ultimatum every 3 days (demand = P × 0.2), two rejections → War with a
        /// 24-hour raid warning. Payment/rejection arrive via universe commands.</summary>
        private void TickRedBannerFriction(Universe universe, HashSet<string> playerBodies, long hour)
        {
            var redBanner = Get(RedBannerId);
            if (redBanner == null || redBanner.Stance == FactionStance.War)
            {
                return;
            }
            bool adjacent = false;
            foreach (string held in redBanner.HeldBodies)
            {
                foreach (string playerBody in playerBodies)
                {
                    if (Hops(held, playerBody) <= 1)
                    {
                        adjacent = true;
                    }
                }
            }
            if (!adjacent)
            {
                return;
            }
            if (redBanner.Stance == FactionStance.Peace)
            {
                redBanner.Stance = FactionStance.Tense;
                redBanner.NextUltimatumHour = hour + (long)UltimatumIntervalDays * GameConstants.HoursPerDay;
                return;
            }
            if (hour >= redBanner.NextUltimatumHour)
            {
                redBanner.NextUltimatumHour = hour + (long)UltimatumIntervalDays * GameConstants.HoursPerDay;
                universe.ActiveWorld.Events.Add(new FactionUltimatumEvent
                {
                    FactionId = redBanner.Id,
                    DemandCredits = (int)(redBanner.P * UltimatumDemandFactor),
                    Count = redBanner.UltimatumsRejected + 1
                });
            }
        }

        /// <summary>Player rejects the current ultimatum; the second rejection is war.</summary>
        public void RejectUltimatum(Universe universe)
        {
            var redBanner = Get(RedBannerId);
            if (redBanner == null || redBanner.Stance != FactionStance.Tense)
            {
                return;
            }
            redBanner.UltimatumsRejected++;
            if (redBanner.UltimatumsRejected >= UltimatumsBeforeWar)
            {
                redBanner.Stance = FactionStance.War;
                redBanner.RaidsBeginHour = universe.Tick / GameConstants.TicksPerHour + WarWarningHours;
                universe.ActiveWorld.Events.Add(new FactionWarDeclaredEvent
                {
                    FactionId = redBanner.Id,
                    RaidsBeginTick = redBanner.RaidsBeginHour * GameConstants.TicksPerHour
                });
            }
        }

        /// <summary>Being attacked embargoes merchant trade (docs/plan/07, M6-T6).</summary>
        public void OnPlayerAttackedFaction(string factionId)
        {
            var faction = Get(factionId);
            if (faction != null)
            {
                faction.Embargoed = true;
                faction.AttitudeToPlayer -= 30;
                if (faction.Personality != FactionPersonality.Merchant)
                {
                    faction.Stance = FactionStance.War;
                }
            }
        }
    }
}
