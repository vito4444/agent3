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

        private int _nextRegionId = 2;
        private int _nextTransitId = 1;
        private List<RecipeM1> _recipes = new List<RecipeM1>();
        private List<TechNode> _techNodes = new List<TechNode>();

        public long Tick => ActiveWorld.Tick;

        public static Universe NewGame(ulong seed, int regionSize)
        {
            var universe = new Universe { Seed = seed };
            universe.ActiveWorld = new World(seed, regionSize);
            universe.ActiveRegionId = 1;
            return universe;
        }

        /// <summary>Recipes/tech are content, not state; the universe re-applies them to
        /// every thawed world.</summary>
        public void SetContent(List<RecipeM1> recipes, List<TechNode> techNodes)
        {
            _recipes = recipes;
            _techNodes = techNodes;
            ApplyContent(ActiveWorld);
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
                if (!ActiveWorld.Buildings.TryGet(padId, out var pad) || !PadPartsComplete(pad))
                {
                    continue;
                }
                Launch(pad);
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
                ArriveTick = ActiveWorld.Tick + (long)Math.Max(1, body.TravelDays) * GameConstants.TicksPerDay
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

        private void Arrive(Transit transit)
        {
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

            // Cargo (or crewed resupply) to an existing region.
            int target = transit.TargetRegionId;
            if (target == ActiveRegionId)
            {
                var pod = ActiveWorld.Buildings.FindFirstOfKind(BuildingKind.CrashPod);
                foreach (var item in transit.Cargo)
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
                foreach (var item in transit.Cargo)
                {
                    slot.Stockpile.TryGetValue(item.ItemId, out int existing);
                    slot.Stockpile[item.ItemId] = existing + item.Count;
                }
            }
            ActiveWorld.Events.Add(new TransitArrivedEvent
            {
                TransitId = transit.Id,
                RegionId = target,
                Payload = transit.Payload
            });
        }

        /// <summary>Queues a cargo rocket to an existing region (docs/plan/06 resupply line).
        /// Used by scenario scripts and the pad order UI.</summary>
        public void QueueCargoTransit(int targetRegionId, string targetBodyId, List<Ingredient> cargo)
        {
            Bodies.TryGet(targetBodyId, out var body);
            Transits.Add(new Transit
            {
                Id = _nextTransitId++,
                FromRegionId = ActiveRegionId,
                TargetBodyId = targetBodyId,
                TargetRegionId = targetRegionId,
                Payload = CargoPodPayload,
                Cargo = cargo,
                DepartTick = ActiveWorld.Tick,
                ArriveTick = ActiveWorld.Tick + (long)Math.Max(1, body?.TravelDays ?? 1) * GameConstants.TicksPerDay
            });
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
                    ["ShortageHours"] = slot.ShortageHours
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
                        ShortageHours = s.Value<int?>("ShortageHours") ?? 0
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
