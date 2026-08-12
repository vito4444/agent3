using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Starsoil.Core
{
    /// <summary>Rocket pad order (docs/plan/06): what to assemble and where to send it.</summary>
    public sealed class PadOrder
    {
        public string TargetBodyId = string.Empty;
        /// <summary>"colonist_pod" or "cargo_pod".</summary>
        public string Payload = string.Empty;
        public int Crew;
        public List<Ingredient> Cargo = new List<Ingredient>();
        public bool Active;
    }

    public sealed class Transit
    {
        public int Id;
        public int FromRegionId;
        public string TargetBodyId = string.Empty;
        /// <summary>0 = open a new region on arrival (colonist pod).</summary>
        public int TargetRegionId;
        public string Payload = string.Empty;
        public int Crew;
        public List<Ingredient> Cargo = new List<Ingredient>();
        public long DepartTick;
        public long ArriveTick;
    }

    /// <summary>A region that is not currently simulated: frozen full save + a coarse
    /// rate model (docs/plan/06 abstract tick).</summary>
    public sealed class RegionSlot
    {
        public int Id;
        public string BodyId = string.Empty;
        public ulong Seed;
        public byte[] FrozenSave;
        /// <summary>Net item units per game hour while abstract (negative = consumption).</summary>
        public Dictionary<string, float> RatesPerHour = new Dictionary<string, float>();
        /// <summary>Abstract inventory (baseline captured at freeze; deltas applied at thaw).</summary>
        public Dictionary<string, int> Stockpile = new Dictionary<string, int>();
        public Dictionary<string, int> Baseline = new Dictionary<string, int>();
        public Dictionary<string, float> FractionAccum = new Dictionary<string, float>();
        public int ColonistCount;
        public int PendingDeaths;
        public int ShortageHours;
        /// <summary>Region has a landing beacon (zero cargo loss, M5-T5).</summary>
        public bool HasBeacon;
    }

    public sealed class RocketLaunchedEvent : ISimEvent
    {
        public int TransitId;
        public string TargetBodyId;
        public string Payload;
    }

    public sealed class TransitArrivedEvent : ISimEvent
    {
        public int TransitId;
        public int RegionId;
        public string Payload;
    }

    public sealed class AbstractShortageEvent : ISimEvent
    {
        public int RegionId;
        public string ItemId;
    }

    public sealed class CargoLossEvent : ISimEvent
    {
        public int TransitId;
        public int RegionId;
        public int UnitsLost;
    }

    /// <summary>Landing on a faction home body raises a warning (M5-T10 placeholder; M6 behavior).</summary>
    public sealed class FactionWarningEvent : ISimEvent
    {
        public string BodyId;
    }

    /// <summary>Automated supply route (M5-T7): keeps the destination stocked above thresholds.</summary>
    public sealed class TradeRoute
    {
        public int Id;
        public int FromRegionId;
        public int ToRegionId;
        public string ToBodyId = string.Empty;
        public List<Ingredient> Thresholds = new List<Ingredient>();
        public bool Suspended;
    }

    public sealed class RouteSuspendedEvent : ISimEvent
    {
        public int RouteId;
        public string Reason;
    }

    /// <summary>
    /// M4 multi-region layer (docs/plan/06): exactly one region runs the full simulation;
    /// the rest are frozen saves advanced by a coarse rate model each game hour. Rockets
    /// assembled on launch pads create transits; colonist pods open new regions on other
    /// bodies of the 曦光 system.
    /// </summary>
    public sealed class Universe
    {
        public const string ColonistPodPayload = "colonist_pod";
        public const string CargoPodPayload = "cargo_pod";
        private const int LaunchWindowHours = 6;
        private const int ShortageGraceHours = 24;
        private const int LandingCrewDefault = 2;

        public ulong Seed { get; private set; }
        public BodyCatalog Bodies { get; } = new BodyCatalog();
        public World ActiveWorld { get; private set; }
        public int ActiveRegionId { get; private set; }
        public string ActiveBodyId { get; private set; } = "dustloam";
        public Dictionary<int, RegionSlot> FrozenRegions { get; } = new Dictionary<int, RegionSlot>();
        public List<Transit> Transits { get; } = new List<Transit>();
        public List<TradeRoute> Routes { get; } = new List<TradeRoute>();
        public FactionSystem FactionsSandbox { get; } = new FactionSystem();
        /// <summary>Player credit balance (星币, docs/plan/06 trade).</summary>
        public double PlayerCredits;
        private Dictionary<string, double> _prices = new Dictionary<string, double>();
        /// <summary>Comms array built anywhere → faction layer visible on the map (M5-T8).</summary>
        public bool FactionLayerVisible;
        private int _nextRouteId = 1;

        private int _nextRegionId = 2;
        private int _nextTransitId = 1;
        private List<RecipeM1> _recipes = new List<RecipeM1>();
        private List<TechNode> _techNodes = new List<TechNode>();

        public long Tick => ActiveWorld.Tick;

        /// <summary>Transfer time (docs/plan/06): same body 1h, adjacent orbit 2h,
        /// +4h per additional hop.</summary>
        public long TransferTicks(string fromBodyId, string toBodyId)
        {
            if (fromBodyId == toBodyId)
            {
                return GameConstants.TicksPerHour;
            }
            Bodies.TryGet(fromBodyId, out var from);
            Bodies.TryGet(toBodyId, out var to);
            int hops = Math.Abs((from?.OrbitIndex ?? 1) - (to?.OrbitIndex ?? 1));
            hops = Math.Max(1, hops);
            long hours = 2 + (hops - 1) * 4L;
            return hours * GameConstants.TicksPerHour;
        }

        public static Universe NewGame(ulong seed, int regionSize)
        {
            var universe = new Universe { Seed = seed };
            universe.ActiveWorld = new World(seed, regionSize);
            universe.ActiveRegionId = 1;
            universe.FactionsSandbox.InitDefault();
            return universe;
        }

        /// <summary>Bodies currently colonized by the player (active + frozen regions).</summary>
        public HashSet<string> PlayerBodies()
        {
            var bodies = new HashSet<string> { ActiveBodyId };
            foreach (var slot in FrozenRegions.Values)
            {
                bodies.Add(slot.BodyId);
            }
            return bodies;
        }

        /// <summary>Recipes/tech are content, not state; the universe re-applies them to
        /// every thawed world.</summary>
        public void SetContent(List<RecipeM1> recipes, List<TechNode> techNodes)
        {
            _recipes = recipes;
            _techNodes = techNodes;
            ApplyContent(ActiveWorld);
        }

        public void SetPrices(Dictionary<string, double> prices)
        {
            _prices = prices ?? new Dictionary<string, double>();
        }

        private const double UnpricedFallback = 1.0;

        /// <summary>Baseline price (GeneratedData/prices.json); 1 for unpriced items.</summary>
        public double PriceOf(string itemId)
        {
            return _prices.TryGetValue(itemId, out double price) ? price : UnpricedFallback;
        }

        private void ApplyContent(World world)
        {
            if (_recipes.Count > 0)
            {
                world.Crafting.LoadRecipes(_recipes);
            }
            if (_techNodes.Count > 0)
            {
                world.Tech.LoadNodes(_techNodes);
            }
            if (Bodies.TryGet(world == ActiveWorld ? ActiveBodyId : "dustloam", out var body))
            {
                world.Body = body;
            }
        }

        public void Step()
        {
            ActiveWorld.Step();
            if (ActiveWorld.Tick % GameConstants.TicksPerHour == 0)
            {
                foreach (var slot in SortedSlots())
                {
                    AbstractHour(slot);
                }
                TickTransits();
                TickRoutes();
                RefreshFactionLayer();
                FactionsSandbox.HourlyTick(this, PlayerBodies());
            }
            TickLaunches();
        }

        // ---------------------------------------------------------------- rockets

        /// <summary>Parts one rocket needs on the pad (docs/plan/06 v1 rocket).</summary>
        public static readonly Ingredient[] RocketParts =
        {
            new Ingredient { ItemId = "rocket_frame_1", Count = 1 },
            new Ingredient { ItemId = "chem_engine", Count = 1 },
            new Ingredient { ItemId = "fuel_tank_module", Count = 1 },
            new Ingredient { ItemId = "fairing", Count = 1 },
            new Ingredient { ItemId = "nav_pod", Count = 1 },
            new Ingredient { ItemId = "rocket_fuel", Count = 4 }
        };

        public static string PayloadItem(string payload)
        {
            return payload == ColonistPodPayload ? "colonist_pod" : "cargo_pod";
        }

        /// <summary>Launch checklist (M5-T3): parts / fuel / payload / target / landing.
        /// All five must pass before a window fires the rocket.</summary>
        public (bool parts, bool fuel, bool payload, bool target, bool landing) PadChecklist(BuildingState pad)
        {
            bool parts = true;
            bool fuel = false;
            foreach (var part in RocketParts)
            {
                if (part.ItemId == "rocket_fuel")
                {
                    fuel = pad.Stock.Get(part.ItemId) >= part.Count;
                }
                else if (pad.Stock.Get(part.ItemId) < part.Count)
                {
                    parts = false;
                }
            }
            bool payload = pad.Pad != null && pad.Stock.Get(PayloadItem(pad.Pad.Payload)) >= 1;
            bool target = pad.Pad != null && Bodies.TryGet(pad.Pad.TargetBodyId, out var body) && body.Landable;
            bool landing = target;
            return (parts, fuel, payload, target, landing);
        }

        public static bool PadPartsComplete(BuildingState pad)
        {
            if (pad.Pad == null || !pad.Pad.Active)
            {
                return false;
            }
            foreach (var part in RocketParts)
            {
                if (pad.Stock.Get(part.ItemId) < part.Count)
                {
                    return false;
                }
            }
            return pad.Stock.Get(PayloadItem(pad.Pad.Payload)) >= 1;
        }

        private void TickLaunches()
        {
            if (ActiveWorld.Tick % (LaunchWindowHours * GameConstants.TicksPerHour) != 0)
            {
                return;
            }
            var padIds = new List<int>();
            foreach (var pair in ActiveWorld.Buildings.All)
            {
                if (pair.Value.DefId == BuildingDefs.LaunchPadId)
                {
                    padIds.Add(pair.Key);
                }
            }
            padIds.Sort();
            foreach (int padId in padIds)
            {
                if (!ActiveWorld.Buildings.TryGet(padId, out var pad) || pad.Pad == null || !pad.Pad.Active)
                {
                    continue;
                }
                var checklist = PadChecklist(pad);
                if (checklist.parts && checklist.fuel && checklist.payload && checklist.target && checklist.landing)
                {
                    Launch(pad);
                }
            }
        }

        private void Launch(BuildingState pad)
        {
            var order = pad.Pad;
            if (!Bodies.TryGet(order.TargetBodyId, out var body) || !body.Landable)
            {
                order.Active = false;
                return;
            }
            foreach (var part in RocketParts)
            {
                pad.Stock.TryRemove(part.ItemId, part.Count);
            }
            pad.Stock.TryRemove(PayloadItem(order.Payload), 1);

            var transit = new Transit
            {
                Id = _nextTransitId,
                FromRegionId = ActiveRegionId,
                TargetBodyId = order.TargetBodyId,
                Payload = order.Payload,
                Crew = order.Payload == ColonistPodPayload ? Math.Max(1, order.Crew) : 0,
                Cargo = new List<Ingredient>(order.Cargo),
                DepartTick = ActiveWorld.Tick,
                ArriveTick = ActiveWorld.Tick + TransferTicks(ActiveBodyId, order.TargetBodyId)
            };
            _nextTransitId++;

            // Crew boards from the active region's population (lowest ids first).
            if (transit.Crew > 0)
            {
                var boarded = new List<int>();
                foreach (var colonist in ActiveWorld.Colonists.AllSorted())
                {
                    if (colonist.Alive && boarded.Count < transit.Crew)
                    {
                        boarded.Add(colonist.Id);
                    }
                }
                transit.Crew = boarded.Count;
                foreach (int id in boarded)
                {
                    ActiveWorld.Colonists.RemoveColonist(ActiveWorld, id);
                }
            }
            // Cargo loads from pad stock (whatever was hauled in beyond the parts).
            var loaded = new List<Ingredient>();
            foreach (var entry in pad.Stock.SortedEntries())
            {
                loaded.Add(new Ingredient { ItemId = entry.Key, Count = entry.Value });
            }
            foreach (var item in loaded)
            {
                pad.Stock.TryRemove(item.ItemId, item.Count);
            }
            transit.Cargo.AddRange(loaded);

            order.Active = false;
            Transits.Add(transit);
            ActiveWorld.Events.Add(new RocketLaunchedEvent
            {
                TransitId = transit.Id,
                TargetBodyId = transit.TargetBodyId,
                Payload = transit.Payload
            });
        }

        private void TickTransits()
        {
            for (int i = 0; i < Transits.Count; i++)
            {
                var transit = Transits[i];
                if (ActiveWorld.Tick < transit.ArriveTick)
                {
                    continue;
                }
                Transits.RemoveAt(i);
                i--;
                Arrive(transit);
            }
        }

        /// <summary>Cargo-loss factor without a landing beacon (M5-T5: fixed edge drop, 5% loss).</summary>
        private const float NoBeaconLossFactor = 0.05f;

        private void Arrive(Transit transit)
        {
            // Landing on a faction home body raises a warning placeholder (M5-T10 → M6).
            if (transit.TargetBodyId == "redridge" || transit.TargetBodyId == "warmmarsh" ||
                transit.TargetBodyId == "sleetfall")
            {
                ActiveWorld.Events.Add(new FactionWarningEvent { BodyId = transit.TargetBodyId });
            }

            if (transit.Payload == ColonistPodPayload && transit.TargetRegionId == 0)
            {
                int regionId = _nextRegionId;
                _nextRegionId++;
                Bodies.TryGet(transit.TargetBodyId, out var body);
                ulong regionSeed = Fnv1a64.HashString(Seed + ":" + transit.TargetBodyId + ":" + regionId);
                var world = World.CreateLandingRegion(regionSeed, GameConstants.DefaultRegionSize, body,
                    transit.Crew, transit.Cargo);
                ApplyContentTo(world, transit.TargetBodyId);
                var slot = Freeze(regionId, transit.TargetBodyId, regionSeed, world);
                FrozenRegions.Add(regionId, slot);
                ActiveWorld.Events.Add(new TransitArrivedEvent
                {
                    TransitId = transit.Id,
                    RegionId = regionId,
                    Payload = transit.Payload
                });
                return;
            }

            // Cargo to region 0 = bonded-warehouse sale at the merchant home (M6-T7).
            if (transit.Payload == CargoPodPayload && transit.TargetRegionId == 0)
            {
                double credits = FactionsSandbox.BondedSaleValue(this, transit.Cargo);
                PlayerCredits += credits;
                var merchantFaction = FactionsSandbox.Get(FactionSystem.MerchantId);
                if (merchantFaction != null)
                {
                    merchantFaction.AttitudeToPlayer += 2;
                }
                ActiveWorld.Events.Add(new DealSettledEvent { QuoteId = 0, Credits = credits });
                return;
            }

            // Cargo (or crewed resupply) to an existing region. Without a landing beacon
            // the pod drops at a fixed edge point and 5% of each stack is lost (M5-T5).
            int target = transit.TargetRegionId;
            bool hasBeacon = DestinationHasBeacon(target);
            int lost = 0;
            var delivered = new List<Ingredient>();
            foreach (var item in transit.Cargo)
            {
                int units = item.Count;
                if (!hasBeacon)
                {
                    int loss = (int)Math.Floor(units * NoBeaconLossFactor);
                    units -= loss;
                    lost += loss;
                }
                if (units > 0)
                {
                    delivered.Add(new Ingredient { ItemId = item.ItemId, Count = units });
                }
            }

            if (target == ActiveRegionId)
            {
                var pod = ActiveWorld.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
                foreach (var item in delivered)
                {
                    if (pod != null)
                    {
                        pod.Stock.Add(item.ItemId, item.Count);
                    }
                    else
                    {
                        ActiveWorld.Piles.Drop(item.ItemId, item.Count, ActiveWorld.StartX, ActiveWorld.StartY);
                    }
                }
            }
            else if (FrozenRegions.TryGetValue(target, out var slot))
            {
                foreach (var item in delivered)
                {
                    slot.Stockpile.TryGetValue(item.ItemId, out int existing);
                    slot.Stockpile[item.ItemId] = existing + item.Count;
                }
            }
            if (lost > 0)
            {
                ActiveWorld.Events.Add(new CargoLossEvent { TransitId = transit.Id, RegionId = target, UnitsLost = lost });
            }
            ActiveWorld.Events.Add(new TransitArrivedEvent
            {
                TransitId = transit.Id,
                RegionId = target,
                Payload = transit.Payload
            });
        }

        private bool DestinationHasBeacon(int regionId)
        {
            if (regionId == ActiveRegionId)
            {
                foreach (var pair in ActiveWorld.Buildings.All)
                {
                    if (pair.Value.DefId == BuildingDefs.LandingBeaconId)
                    {
                        return true;
                    }
                }
                return false;
            }
            return FrozenRegions.TryGetValue(regionId, out var slot) && slot.HasBeacon;
        }

        /// <summary>Queues a cargo rocket to an existing region (docs/plan/06 resupply line).
        /// Used by scenario scripts and the pad order UI.</summary>
        public void QueueCargoTransit(int targetRegionId, string targetBodyId, List<Ingredient> cargo)
        {
            Transits.Add(new Transit
            {
                Id = _nextTransitId++,
                FromRegionId = ActiveRegionId,
                TargetBodyId = targetBodyId,
                TargetRegionId = targetRegionId,
                Payload = CargoPodPayload,
                Cargo = cargo,
                DepartTick = ActiveWorld.Tick,
                ArriveTick = ActiveWorld.Tick + TransferTicks(ActiveBodyId, targetBodyId)
            });
        }

        /// <summary>Creates an automated supply route (M5-T7).</summary>
        public TradeRoute AddRoute(int fromRegionId, int toRegionId, string toBodyId, List<Ingredient> thresholds)
        {
            var route = new TradeRoute
            {
                Id = _nextRouteId,
                FromRegionId = fromRegionId,
                ToRegionId = toRegionId,
                ToBodyId = toBodyId,
                Thresholds = thresholds
            };
            _nextRouteId++;
            Routes.Add(route);
            return route;
        }

        /// <summary>Route upkeep: when the destination drops below a threshold, load the
        /// shortfall plus one rocket_fuel from the origin and queue a cargo transit.
        /// Missing fuel or goods suspends the route with an alert (M5-T7).</summary>
        private void TickRoutes()
        {
            foreach (var route in Routes)
            {
                if (route.Suspended)
                {
                    continue;
                }
                if (route.FromRegionId != ActiveRegionId)
                {
                    TickFrozenOriginRoute(route);
                    continue;
                }
                bool inFlight = false;
                foreach (var transit in Transits)
                {
                    if (transit.TargetRegionId == route.ToRegionId && transit.Payload == CargoPodPayload)
                    {
                        inFlight = true;
                    }
                }
                if (inFlight)
                {
                    continue;
                }

                var shortfall = new List<Ingredient>();
                foreach (var threshold in route.Thresholds)
                {
                    int destStock = DestinationStock(route, threshold.ItemId);
                    if (destStock < threshold.Count)
                    {
                        shortfall.Add(new Ingredient { ItemId = threshold.ItemId, Count = threshold.Count - destStock });
                    }
                }
                if (shortfall.Count == 0)
                {
                    continue;
                }

                if (ActiveWorld.CountItemEverywhere("rocket_fuel") < 1)
                {
                    route.Suspended = true;
                    ActiveWorld.Events.Add(new RouteSuspendedEvent { RouteId = route.Id, Reason = "fuel" });
                    ActiveWorld.Alerts.Raise(ActiveWorld, "route_suspended_" + route.Id, AlertSeverity.Warning,
                        ActiveWorld.PodInteriorX, ActiveWorld.PodInteriorY);
                    continue;
                }
                var loaded = new List<Ingredient>();
                foreach (var item in shortfall)
                {
                    int taken = 0;
                    for (int i = 0; i < item.Count; i++)
                    {
                        if (ActiveWorld.TryConsumeItemAnywhere(item.ItemId))
                        {
                            taken++;
                        }
                    }
                    if (taken > 0)
                    {
                        loaded.Add(new Ingredient { ItemId = item.ItemId, Count = taken });
                    }
                }
                if (loaded.Count == 0)
                {
                    // Goods not stocked yet: wait for production (only missing fuel
                    // suspends a route, M5-T7 wording).
                    continue;
                }
                ActiveWorld.TryConsumeItemAnywhere("rocket_fuel");
                QueueCargoTransit(route.ToRegionId, route.ToBodyId, loaded);
            }
        }

        /// <summary>Routes departing a frozen region draw goods and fuel from its abstract
        /// stockpile (the return leg of a two-way line, M5-T9).</summary>
        private void TickFrozenOriginRoute(TradeRoute route)
        {
            if (!FrozenRegions.TryGetValue(route.FromRegionId, out var origin))
            {
                return;
            }
            bool inFlight = false;
            foreach (var transit in Transits)
            {
                if (transit.TargetRegionId == route.ToRegionId && transit.FromRegionId == route.FromRegionId)
                {
                    inFlight = true;
                }
            }
            if (inFlight)
            {
                return;
            }
            var shortfall = new List<Ingredient>();
            foreach (var threshold in route.Thresholds)
            {
                int destStock = DestinationStock(route, threshold.ItemId);
                if (destStock < threshold.Count)
                {
                    shortfall.Add(new Ingredient { ItemId = threshold.ItemId, Count = threshold.Count - destStock });
                }
            }
            if (shortfall.Count == 0)
            {
                return;
            }
            origin.Stockpile.TryGetValue("rocket_fuel", out int fuel);
            if (fuel < 1)
            {
                route.Suspended = true;
                ActiveWorld.Events.Add(new RouteSuspendedEvent { RouteId = route.Id, Reason = "fuel" });
                return;
            }
            var loaded = new List<Ingredient>();
            foreach (var item in shortfall)
            {
                origin.Stockpile.TryGetValue(item.ItemId, out int stock);
                int taken = Math.Min(stock, item.Count);
                if (taken > 0)
                {
                    origin.Stockpile[item.ItemId] = stock - taken;
                    loaded.Add(new Ingredient { ItemId = item.ItemId, Count = taken });
                }
            }
            if (loaded.Count == 0)
            {
                // Wait for the origin's abstract production to stock up.
                return;
            }
            origin.Stockpile["rocket_fuel"] = fuel - 1;
            Bodies.TryGet(origin.BodyId, out _);
            Transits.Add(new Transit
            {
                Id = _nextTransitId++,
                FromRegionId = route.FromRegionId,
                TargetBodyId = route.ToBodyId,
                TargetRegionId = route.ToRegionId,
                Payload = CargoPodPayload,
                Cargo = loaded,
                DepartTick = ActiveWorld.Tick,
                ArriveTick = ActiveWorld.Tick + TransferTicks(origin.BodyId, route.ToBodyId)
            });
        }

        private int DestinationStock(TradeRoute route, string itemId)
        {
            if (route.ToRegionId == ActiveRegionId)
            {
                return ActiveWorld.CountItemEverywhere(itemId);
            }
            if (FrozenRegions.TryGetValue(route.ToRegionId, out var slot))
            {
                slot.Stockpile.TryGetValue(itemId, out int stock);
                return stock;
            }
            return 0;
        }

        private void RefreshFactionLayer()
        {
            if (FactionLayerVisible)
            {
                return;
            }
            foreach (var pair in ActiveWorld.Buildings.All)
            {
                if (pair.Value.DefId == "comms_array")
                {
                    FactionLayerVisible = true;
                    return;
                }
            }
        }

        // ---------------------------------------------------------------- abstract regions

        private void AbstractHour(RegionSlot slot)
        {
            bool shortage = false;
            foreach (var pair in slot.RatesPerHour)
            {
                slot.FractionAccum.TryGetValue(pair.Key, out float accum);
                accum += pair.Value;
                int whole = (int)Math.Floor(accum);
                accum -= whole;
                slot.FractionAccum[pair.Key] = accum;
                if (whole == 0)
                {
                    continue;
                }
                slot.Stockpile.TryGetValue(pair.Key, out int current);
                int next = current + whole;
                if (next < 0)
                {
                    next = 0;
                    shortage = true;
                    ActiveWorld.Events.Add(new AbstractShortageEvent { RegionId = slot.Id, ItemId = pair.Key });
                }
                slot.Stockpile[pair.Key] = next;
            }

            if (shortage)
            {
                slot.ShortageHours++;
                if (slot.ShortageHours >= ShortageGraceHours && slot.ColonistCount > slot.PendingDeaths)
                {
                    slot.PendingDeaths++;
                    slot.ShortageHours = 0;
                }
            }
            else if (slot.ShortageHours > 0)
            {
                slot.ShortageHours--;
            }
        }

        /// <summary>Switches the fully simulated region (docs/plan/06 M4-T5).</summary>
        public void SwitchActive(int regionId)
        {
            if (regionId == ActiveRegionId || !FrozenRegions.TryGetValue(regionId, out var target))
            {
                return;
            }
            var frozen = Freeze(ActiveRegionId, ActiveBodyId, ActiveWorld.Seed, ActiveWorld);
            FrozenRegions.Remove(regionId);
            FrozenRegions.Add(frozen.Id, frozen);

            ActiveWorld = Thaw(target);
            ActiveRegionId = target.Id;
            ActiveBodyId = target.BodyId;
        }

        private RegionSlot Freeze(int regionId, string bodyId, ulong seed, World world)
        {
            var slot = new RegionSlot
            {
                Id = regionId,
                BodyId = bodyId,
                Seed = seed,
                FrozenSave = SaveSerializer.ToGzipJson(SaveSerializer.Capture(world)),
                ColonistCount = world.Colonists.AliveCount
            };
            foreach (var pair in world.Buildings.All)
            {
                if (pair.Value.DefId == BuildingDefs.LandingBeaconId)
                {
                    slot.HasBeacon = true;
                }
            }
            CollectStock(world, slot.Stockpile);
            foreach (var pair in slot.Stockpile)
            {
                slot.Baseline[pair.Key] = pair.Value;
            }
            DeriveRates(world, slot.RatesPerHour);
            return slot;
        }

        private World Thaw(RegionSlot slot)
        {
            var world = SaveSerializer.Restore(SaveSerializer.FromGzipJson(slot.FrozenSave));
            ApplyContentTo(world, slot.BodyId);

            // Apply abstract-period stock deltas.
            var pod = world.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
            var keys = new List<string>(slot.Stockpile.Keys);
            foreach (var pair in slot.Baseline)
            {
                if (!slot.Stockpile.ContainsKey(pair.Key))
                {
                    keys.Add(pair.Key);
                }
            }
            keys.Sort(StringComparer.Ordinal);
            foreach (string itemId in keys)
            {
                slot.Stockpile.TryGetValue(itemId, out int now);
                slot.Baseline.TryGetValue(itemId, out int before);
                int delta = now - before;
                if (delta > 0 && pod != null)
                {
                    pod.Stock.Add(itemId, delta);
                }
                for (int i = 0; i < -delta; i++)
                {
                    world.TryConsumeItemAnywhere(itemId);
                }
            }
            for (int i = 0; i < slot.PendingDeaths; i++)
            {
                world.Colonists.KillFirstAlive(world);
            }
            return world;
        }

        private void ApplyContentTo(World world, string bodyId)
        {
            if (_recipes.Count > 0)
            {
                world.Crafting.LoadRecipes(_recipes);
            }
            if (_techNodes.Count > 0)
            {
                world.Tech.LoadNodes(_techNodes);
            }
            if (Bodies.TryGet(bodyId, out var body))
            {
                world.Body = body;
            }
        }

        private static void CollectStock(World world, Dictionary<string, int> stock)
        {
            foreach (var pile in world.Piles.All.Values)
            {
                stock.TryGetValue(pile.ItemId, out int v);
                stock[pile.ItemId] = v + pile.Count;
            }
            foreach (var building in world.Buildings.All.Values)
            {
                foreach (var entry in building.Stock.SortedEntries())
                {
                    stock.TryGetValue(entry.Key, out int v);
                    stock[entry.Key] = v + entry.Value;
                }
            }
        }

        /// <summary>
        /// Coarse net rates for the abstract model (docs/plan/06): colonist food/water
        /// consumption minus powered-extractor and maintain-order output estimates.
        /// Deliberately conservative; the full sim is authoritative when the region is active.
        /// </summary>
        private static void DeriveRates(World world, Dictionary<string, float> rates)
        {
            int alive = world.Colonists.AliveCount;
            AddRate(rates, ItemIds.Water, -alive / (float)GameConstants.HoursPerDay);
            AddRate(rates, ItemIds.Ration, -alive / (float)GameConstants.HoursPerDay);

            foreach (var building in world.Buildings.All.Values)
            {
                if (!BuildingDefs.TryGet(building.DefId, out var def) || !def.IsMachine || !building.WantsPower)
                {
                    continue;
                }
                if (!world.Networks.IsPowered(world, building))
                {
                    continue;
                }
                if (def.Extracts.Count > 0)
                {
                    int nodeId = world.Buildings.FindDepositFor(def, building.X, building.Y);
                    if (nodeId != 0 && world.Nodes.TryGet(nodeId, out var node))
                    {
                        AddRate(rates, node.ItemId, GameConstants.TicksPerHour / (float)Balance.ExtractorTicksPerUnit);
                    }
                }
                else
                {
                    var order = world.Crafting.ActiveOrder(world, building);
                    if (order != null && world.Crafting.TryGetRecipe(order.RecipeId, out var recipe))
                    {
                        float craftsPerHour = GameConstants.TicksPerHour / (float)Math.Max(1, recipe.WorkTicks);
                        foreach (var output in recipe.Outputs)
                        {
                            AddRate(rates, output.ItemId, craftsPerHour * output.Count);
                        }
                        foreach (var input in recipe.Inputs)
                        {
                            AddRate(rates, input.ItemId, -craftsPerHour * input.Count);
                        }
                    }
                }
            }
        }

        private static void AddRate(Dictionary<string, float> rates, string itemId, float perHour)
        {
            rates.TryGetValue(itemId, out float v);
            rates[itemId] = v + perHour;
        }

        private List<RegionSlot> SortedSlots()
        {
            var ids = new List<int>(FrozenRegions.Keys);
            ids.Sort();
            var result = new List<RegionSlot>(ids.Count);
            foreach (int id in ids)
            {
                result.Add(FrozenRegions[id]);
            }
            return result;
        }

        // ---------------------------------------------------------------- save/load

        public byte[] ToGzipJson()
        {
            var root = new JObject
            {
                ["Version"] = 1,
                ["Seed"] = Seed,
                ["ActiveRegionId"] = ActiveRegionId,
                ["ActiveBodyId"] = ActiveBodyId,
                ["NextRegionId"] = _nextRegionId,
                ["NextTransitId"] = _nextTransitId,
                ["ActiveBlob"] = Convert.ToBase64String(SaveSerializer.ToGzipJson(SaveSerializer.Capture(ActiveWorld))),
                ["Transits"] = JArray.FromObject(Transits),
                ["Routes"] = JArray.FromObject(Routes),
                ["Factions"] = JArray.FromObject(FactionsSandbox.Factions),
                ["Quotes"] = JArray.FromObject(FactionsSandbox.Quotes),
                ["PendingDeals"] = JArray.FromObject(FactionsSandbox.PendingDeals),
                ["QuoteBoardExpiresHour"] = FactionsSandbox.QuoteBoardExpiresHour,
                ["PlayerCredits"] = PlayerCredits,
                ["NextRouteId"] = _nextRouteId,
                ["FactionLayerVisible"] = FactionLayerVisible,
                ["Slots"] = SlotsJson()
            };
            byte[] raw = Encoding.UTF8.GetBytes(root.ToString(Formatting.None));
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }
                return output.ToArray();
            }
        }

        private JArray SlotsJson()
        {
            var array = new JArray();
            foreach (var slot in SortedSlots())
            {
                array.Add(new JObject
                {
                    ["Id"] = slot.Id,
                    ["BodyId"] = slot.BodyId,
                    ["Seed"] = slot.Seed,
                    ["Blob"] = Convert.ToBase64String(slot.FrozenSave),
                    ["Rates"] = JObject.FromObject(slot.RatesPerHour),
                    ["Stockpile"] = JObject.FromObject(slot.Stockpile),
                    ["Baseline"] = JObject.FromObject(slot.Baseline),
                    ["ColonistCount"] = slot.ColonistCount,
                    ["PendingDeaths"] = slot.PendingDeaths,
                    ["ShortageHours"] = slot.ShortageHours,
                    ["HasBeacon"] = slot.HasBeacon
                });
            }
            return array;
        }

        public static Universe FromGzipJson(byte[] bytes)
        {
            string json;
            using (var input = new MemoryStream(bytes))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
            {
                json = reader.ReadToEnd();
            }
            var root = JObject.Parse(json);
            var universe = new Universe
            {
                Seed = root.Value<ulong>("Seed"),
                ActiveRegionId = root.Value<int>("ActiveRegionId"),
                ActiveBodyId = root.Value<string>("ActiveBodyId") ?? "dustloam",
                _nextRegionId = root.Value<int?>("NextRegionId") ?? 2,
                _nextTransitId = root.Value<int?>("NextTransitId") ?? 1
            };
            universe.ActiveWorld = SaveSerializer.Restore(
                SaveSerializer.FromGzipJson(Convert.FromBase64String(root.Value<string>("ActiveBlob"))));
            if (root["Transits"] is JArray transits)
            {
                foreach (var token in transits)
                {
                    universe.Transits.Add(token.ToObject<Transit>());
                }
            }
            if (root["Routes"] is JArray routes)
            {
                foreach (var token in routes)
                {
                    universe.Routes.Add(token.ToObject<TradeRoute>());
                }
            }
            universe._nextRouteId = root.Value<int?>("NextRouteId") ?? 1;
            universe.FactionLayerVisible = root.Value<bool?>("FactionLayerVisible") ?? false;
            if (root["Factions"] is JArray factions && factions.Count > 0)
            {
                universe.FactionsSandbox.Factions.Clear();
                foreach (var token in factions)
                {
                    universe.FactionsSandbox.Factions.Add(token.ToObject<Faction>());
                }
            }
            else
            {
                universe.FactionsSandbox.InitDefault();
            }
            if (root["Quotes"] is JArray quotes)
            {
                foreach (var token in quotes)
                {
                    universe.FactionsSandbox.Quotes.Add(token.ToObject<MerchantQuote>());
                }
            }
            if (root["PendingDeals"] is JArray deals)
            {
                foreach (var token in deals)
                {
                    universe.FactionsSandbox.PendingDeals.Add(token.ToObject<PendingDeal>());
                }
            }
            universe.FactionsSandbox.QuoteBoardExpiresHour = root.Value<long?>("QuoteBoardExpiresHour") ?? 0;
            universe.PlayerCredits = root.Value<double?>("PlayerCredits") ?? 0;
            if (root["Slots"] is JArray slots)
            {
                foreach (var token in slots)
                {
                    if (!(token is JObject s))
                    {
                        continue;
                    }
                    var slot = new RegionSlot
                    {
                        Id = s.Value<int>("Id"),
                        BodyId = s.Value<string>("BodyId") ?? string.Empty,
                        Seed = s.Value<ulong>("Seed"),
                        FrozenSave = Convert.FromBase64String(s.Value<string>("Blob")),
                        ColonistCount = s.Value<int?>("ColonistCount") ?? 0,
                        PendingDeaths = s.Value<int?>("PendingDeaths") ?? 0,
                        ShortageHours = s.Value<int?>("ShortageHours") ?? 0,
                        HasBeacon = s.Value<bool?>("HasBeacon") ?? false
                    };
                    ReadIntMap(s["Stockpile"] as JObject, slot.Stockpile);
                    ReadIntMap(s["Baseline"] as JObject, slot.Baseline);
                    if (s["Rates"] is JObject rates)
                    {
                        foreach (var pair in rates)
                        {
                            slot.RatesPerHour[pair.Key] = pair.Value.Value<float>();
                        }
                    }
                    universe.FrozenRegions.Add(slot.Id, slot);
                }
            }
            return universe;
        }

        private static void ReadIntMap(JObject source, Dictionary<string, int> target)
        {
            if (source == null)
            {
                return;
            }
            foreach (var pair in source)
            {
                target[pair.Key] = pair.Value.Value<int>();
            }
        }
    }
}
