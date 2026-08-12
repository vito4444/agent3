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

    public sealed class MerchantQuote
    {
        public int Id;
        public string ItemId = string.Empty;
        public int Count;
        public double UnitPrice;
        /// <summary>True: the merchant sells (player buys). False: the merchant buys.</summary>
        public bool MerchantSells;
        public long ExpiresHour;
    }

    public sealed class PendingDeal
    {
        public int QuoteId;
        public string ItemId = string.Empty;
        public int Count;
        public double Total;
        public bool MerchantSells;
        public long SettleHour;
    }

    public sealed class QuoteBoardRefreshedEvent : ISimEvent
    {
        public int QuoteCount;
    }

    public sealed class DealSettledEvent : ISimEvent
    {
        public int QuoteId;
        public double Credits;
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
        // Personality parameter table (docs/plan/07 性格参数表).
        private const float MerchantGrowth = 1.0f;
        private const float MerchantTech = 1.0f;
        private const float MerchantDefense = 1.0f;
        private const float RedBannerGrowth = 1.3f;
        private const float RedBannerTech = 0.8f;
        private const float RedBannerDefense = 1.2f;
        private const float SilentGrowth = 1.0f;
        private const float SilentTech = 1.6f;
        private const float SilentDefense = 2.5f;
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
        public List<MerchantQuote> Quotes { get; } = new List<MerchantQuote>();
        public List<PendingDeal> PendingDeals { get; } = new List<PendingDeal>();
        public long QuoteBoardExpiresHour;
        private int _nextQuoteId = 1;

        private const int QuoteRefreshHours = 12;
        private const int DealSettleHours = 6;
        private const double SellMarkup = 1.3;
        private const double BuyMarkdown = 0.9;
        private const double BondedBonus = 1.1;
        private const int RareAttitudeGate = 60;

        private static readonly string[] MerchantSellPool =
        {
            "myco_gold_spore", "spore_protein", "fungal_timber", "methane_ice", "algae_seed", "biomass"
        };

        private static readonly string[] MerchantBuyPool =
        {
            "steel_plate", "basic_circuit", "water", "ration", "copper_wire", "glass"
        };

        private static readonly string[] MerchantRarePool =
        {
            "platinum_sand", "helium3", "deuterium_ice"
        };

        public void InitDefault()
        {
            Factions.Clear();
            Factions.Add(new Faction
            {
                Id = MerchantId, Zh = "星贸商盟", En = "Star Trade Compact",
                Personality = FactionPersonality.Merchant,
                P = StartPower, GrowthMult = MerchantGrowth, TechMult = MerchantTech, DefenseMult = MerchantDefense,
                HeldBodies = { "warmmarsh" },
                AttitudeToPlayer = 10
            });
            Factions.Add(new Faction
            {
                Id = RedBannerId, Zh = "赤旗军团", En = "Red Banner Legion",
                Personality = FactionPersonality.Expansionist,
                P = StartPower, GrowthMult = RedBannerGrowth, TechMult = RedBannerTech, DefenseMult = RedBannerDefense,
                HeldBodies = { "redridge" },
                AttitudeToPlayer = -10
            });
            Factions.Add(new Faction
            {
                Id = SilentId, Zh = "静默会", En = "The Silent Accord",
                Personality = FactionPersonality.Reclusive,
                P = StartPower, GrowthMult = SilentGrowth, TechMult = SilentTech, DefenseMult = SilentDefense,
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
            TickMerchantQuotes(universe, hour);
            SettleDeals(universe, hour);
        }

        // ---------------------------------------------------------------- merchant trade

        /// <summary>Quote board (M6-T6): needs the player's comms array and non-negative
        /// attitude; refreshes every 12 game hours with 3 sell + 3 buy (myco spores always
        /// on the sell side); attitude ≥60 adds a rare listing; embargo while attacked.</summary>
        private void TickMerchantQuotes(Universe universe, long hour)
        {
            var merchant = Get(MerchantId);
            if (merchant == null || merchant.Embargoed || merchant.AttitudeToPlayer < 0 ||
                !universe.FactionLayerVisible)
            {
                return;
            }
            if (Quotes.Count > 0 && hour < QuoteBoardExpiresHour)
            {
                return;
            }
            Quotes.Clear();
            QuoteBoardExpiresHour = hour + QuoteRefreshHours;
            ulong roll = Fnv1a64.HashString(universe.Seed + ":quotes:" + hour);

            AddQuote(universe, "myco_gold_spore", merchantSells: true, roll);
            AddQuote(universe, MerchantSellPool[(int)(roll % (ulong)MerchantSellPool.Length)], true, roll * 31UL);
            AddQuote(universe, MerchantSellPool[(int)((roll / 7UL) % (ulong)MerchantSellPool.Length)], true, roll * 131UL);
            for (int i = 0; i < 3; i++)
            {
                AddQuote(universe, MerchantBuyPool[(int)((roll / (ulong)(11 + i * 3)) % (ulong)MerchantBuyPool.Length)],
                    false, roll * (ulong)(17 + i));
            }
            if (merchant.AttitudeToPlayer >= RareAttitudeGate)
            {
                AddQuote(universe, MerchantRarePool[(int)(roll % (ulong)MerchantRarePool.Length)], true, roll * 977UL);
            }
            universe.ActiveWorld.Events.Add(new QuoteBoardRefreshedEvent { QuoteCount = Quotes.Count });
        }

        private void AddQuote(Universe universe, string itemId, bool merchantSells, ulong roll)
        {
            double basePrice = universe.PriceOf(itemId);
            var quote = new MerchantQuote
            {
                Id = _nextQuoteId,
                ItemId = itemId,
                Count = 4 + (int)(roll % 9UL),
                UnitPrice = System.Math.Round(basePrice * (merchantSells ? SellMarkup : BuyMarkdown), 2),
                MerchantSells = merchantSells,
                ExpiresHour = QuoteBoardExpiresHour
            };
            _nextQuoteId++;
            Quotes.Add(quote);
        }

        /// <summary>Accepts a quote; goods/credits settle after 6 game hours (M6-T6).</summary>
        public bool AcceptQuote(Universe universe, int quoteId)
        {
            MerchantQuote quote = null;
            foreach (var candidate in Quotes)
            {
                if (candidate.Id == quoteId)
                {
                    quote = candidate;
                }
            }
            long hour = universe.Tick / GameConstants.TicksPerHour;
            if (quote == null || hour >= quote.ExpiresHour)
            {
                return false;
            }
            double total = quote.UnitPrice * quote.Count;
            if (quote.MerchantSells)
            {
                if (universe.PlayerCredits < total)
                {
                    return false;
                }
                universe.PlayerCredits -= total;
            }
            else
            {
                int removed = 0;
                for (int i = 0; i < quote.Count; i++)
                {
                    if (universe.ActiveWorld.TryConsumeItemAnywhere(quote.ItemId))
                    {
                        removed++;
                    }
                }
                if (removed < quote.Count)
                {
                    // Partial stock: settle what was actually loaded.
                    total = quote.UnitPrice * removed;
                    quote.Count = removed;
                    if (removed == 0)
                    {
                        return false;
                    }
                }
            }
            Quotes.Remove(quote);
            PendingDeals.Add(new PendingDeal
            {
                QuoteId = quote.Id,
                ItemId = quote.ItemId,
                Count = quote.Count,
                Total = total,
                MerchantSells = quote.MerchantSells,
                SettleHour = hour + DealSettleHours
            });
            return true;
        }

        private void SettleDeals(Universe universe, long hour)
        {
            for (int i = 0; i < PendingDeals.Count; i++)
            {
                var deal = PendingDeals[i];
                if (hour < deal.SettleHour)
                {
                    continue;
                }
                PendingDeals.RemoveAt(i);
                i--;
                var merchant = Get(MerchantId);
                if (deal.MerchantSells)
                {
                    var pod = universe.ActiveWorld.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
                    if (pod != null)
                    {
                        pod.Stock.Add(deal.ItemId, deal.Count);
                    }
                }
                else
                {
                    universe.PlayerCredits += deal.Total;
                }
                if (merchant != null)
                {
                    merchant.AttitudeToPlayer += 2;
                    merchant.Treasury += deal.MerchantSells ? (float)deal.Total : -(float)deal.Total;
                }
                universe.ActiveWorld.Events.Add(new DealSettledEvent { QuoteId = deal.QuoteId, Credits = deal.Total });
            }
        }

        /// <summary>Bonded-warehouse sale (M6-T7): goods delivered to the merchant's home
        /// settle at 10% above the quote-board buy price.</summary>
        public double BondedSaleValue(Universe universe, List<Ingredient> cargo)
        {
            double total = 0;
            foreach (var item in cargo)
            {
                total += universe.PriceOf(item.ItemId) * BuyMarkdown * BondedBonus * item.Count;
            }
            return System.Math.Round(total, 2);
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
