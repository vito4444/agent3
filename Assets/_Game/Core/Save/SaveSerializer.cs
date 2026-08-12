using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Starsoil.Core
{
    /// <summary>
    /// Versioned migrations applied to the raw JSON before deserialization
    /// (docs/plan/08 save policy: schema version + chained migration registry).
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>fromVersion → mutation that upgrades the JSON to fromVersion+1.</summary>
        private static readonly Dictionary<int, Action<JObject>> Registry = new Dictionary<int, Action<JObject>>
        {
            // v0 (M0 skeleton: terrain + buildings only) → v1 (M1 colony): new collections
            // default to empty; buildings gain durability/stock defaults.
            { 0, MigrateV0ToV1 },
            // v1 (M1) → v2 (M2 networks/machines): new building fields (WantsPower,
            // ProcessAccum, BatteryKwh) and gas components deserialize to their class
            // defaults, so the migration only bumps the version.
            { 1, _ => { } },
            // v2 → v3 (combat entities): unit/wave lists default to empty.
            { 2, _ => { } }
        };

        public static void UpgradeInPlace(JObject root)
        {
            int version = root.Value<int?>("SchemaVersion") ?? 0;
            while (version < GameConstants.SaveSchemaVersion)
            {
                if (!Registry.TryGetValue(version, out var migration))
                {
                    throw new InvalidDataException("No migration registered from save schema " + version);
                }
                migration(root);
                version++;
                root["SchemaVersion"] = version;
            }
            if (version > GameConstants.SaveSchemaVersion)
            {
                throw new InvalidDataException(
                    "Save schema " + version + " is newer than supported " + GameConstants.SaveSchemaVersion);
            }
        }

        private static void MigrateV0ToV1(JObject root)
        {
            if (root["Buildings"] is JArray buildings)
            {
                foreach (var token in buildings)
                {
                    if (token is JObject building)
                    {
                        building["Durability"] = building["Durability"] ?? Balance.NeedMax;
                        building["StaffedRequested"] = building["StaffedRequested"] ?? false;
                    }
                }
            }
            // Remaining v1 fields deserialize to their defaults (empty lists / zeros);
            // a v0 dev save therefore loads as a colony with no colonists.
        }
    }

    /// <summary>
    /// gzip(JSON) save pipeline (docs/plan/08). Binary is only considered if measured
    /// main-thread stalls exceed the plan's 200ms trigger.
    /// </summary>
    public static class SaveSerializer
    {
        public static SaveData Capture(World world)
        {
            var data = new SaveData
            {
                Seed = world.Seed,
                Tick = world.Tick,
                RegionSize = world.Terrain.Size,
                TerrainHeights = world.Terrain.CopyHeights(),
                StartX = world.StartX,
                StartY = world.StartY,
                PodInteriorX = world.PodInteriorX,
                PodInteriorY = world.PodInteriorY,
                GraveX = world.GraveX,
                GraveY = world.GraveY,
                Defeated = world.Defeated,
                O2Tank = world.Life.TankO2,
                StormState = (int)world.Storm.State,
                StormNextStartTick = world.Storm.NextStartTick,
                StormEndTick = world.Storm.EndTick,
                TutorialStep = world.Tutorial.StepIndex,
                TutorialSkipped = world.Tutorial.Skipped,
                StatsMined = SortedCopy(world.Stats.Mined),
                StatsCrafted = SortedCopy(world.Stats.Crafted),
                StatsBuilt = SortedCopy(world.Stats.Built),
                StatsDeaths = SortedCopy(world.Stats.Deaths),
                StatsBurials = world.Stats.Burials
            };

            foreach (int id in SortedKeys(world.Buildings.All.Keys))
            {
                var b = world.Buildings.All[id];
                var saved = new SavedBuilding
                {
                    Id = b.Id,
                    DefId = b.DefId,
                    X = b.X,
                    Y = b.Y,
                    Rotation = b.Rotation,
                    Durability = b.Durability,
                    StaffedRequested = b.StaffedRequested,
                    WantsPower = b.WantsPower,
                    ProcessAccum = b.ProcessAccum,
                    BatteryKwh = b.BatteryKwh,
                    Stock = StacksOf(b.Stock)
                };
                if (b.Pad != null)
                {
                    saved.PadTargetBody = b.Pad.TargetBodyId;
                    saved.PadPayload = b.Pad.Payload;
                    saved.PadCrew = b.Pad.Crew;
                    saved.PadActive = b.Pad.Active;
                    saved.PadCargo = new List<SavedStack>();
                    foreach (var item in b.Pad.Cargo)
                    {
                        saved.PadCargo.Add(new SavedStack { ItemId = item.ItemId, Count = item.Count });
                    }
                }
                data.Buildings.Add(saved);
            }

            // Carried loads normalize into ground piles at the carrier's cell so the
            // capture is complete without persisting transient task state.
            var carriedByCell = new Dictionary<(int x, int y, string item), int>();
            foreach (int id in SortedKeys(world.Colonists.All.Keys))
            {
                var c = world.Colonists.All[id];
                if (c.CarryingCount > 0)
                {
                    var key = (c.X, c.Y, c.CarryingItem);
                    carriedByCell.TryGetValue(key, out int existing);
                    carriedByCell[key] = existing + c.CarryingCount;
                }
                data.Colonists.Add(new SavedColonist
                {
                    Id = c.Id,
                    X = c.X,
                    Y = c.Y,
                    O2 = c.O2,
                    Water = c.Water,
                    Food = c.Food,
                    Sleep = c.Sleep,
                    Temp = c.Temp,
                    BottleO2 = c.BottleO2,
                    Alive = c.Alive,
                    CriticalCause = (int)c.CriticalCause,
                    CriticalTicksLeft = c.CriticalTicksLeft,
                    FaintTicksLeft = c.FaintTicksLeft,
                    Job = (int)c.Job,
                    Morale = c.Morale,
                    MoraleEventOffset = c.MoraleEventOffset,
                    OnStrike = c.OnStrike,
                    FoodVarietyYesterday = c.FoodVarietyYesterday,
                    NightWorkHours = c.NightWorkHours
                });
            }

            foreach (int id in SortedKeys(world.Piles.All.Keys))
            {
                var pile = world.Piles.All[id];
                data.Piles.Add(new SavedPile { Id = pile.Id, ItemId = pile.ItemId, Count = pile.Count, X = pile.X, Y = pile.Y });
            }
            int nextPileId = 1;
            foreach (var pile in data.Piles)
            {
                nextPileId = Math.Max(nextPileId, pile.Id + 1);
            }
            var carriedKeys = new List<(int x, int y, string item)>(carriedByCell.Keys);
            carriedKeys.Sort((a, b) =>
            {
                int cmp = a.x.CompareTo(b.x);
                if (cmp != 0) return cmp;
                cmp = a.y.CompareTo(b.y);
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.item, b.item);
            });
            foreach (var key in carriedKeys)
            {
                data.Piles.Add(new SavedPile
                {
                    Id = nextPileId,
                    ItemId = key.item,
                    Count = carriedByCell[key],
                    X = key.x,
                    Y = key.y
                });
                nextPileId++;
            }

            foreach (int id in SortedKeys(world.Nodes.All.Keys))
            {
                var node = world.Nodes.All[id];
                data.Nodes.Add(new SavedNode
                {
                    Id = node.Id,
                    ItemId = node.ItemId,
                    Remaining = node.Remaining,
                    X = node.X,
                    Y = node.Y,
                    Designated = node.Designated,
                    TicksPerUnit = node.TicksPerUnit,
                    FactionLocked = node.FactionLocked
                });
            }

            foreach (int id in SortedKeys(world.Blueprints.All.Keys))
            {
                var bp = world.Blueprints.All[id];
                data.Blueprints.Add(new SavedBlueprint
                {
                    Id = bp.Id,
                    DefId = bp.DefId,
                    X = bp.X,
                    Y = bp.Y,
                    Rotation = bp.Rotation,
                    BuildProgress = bp.BuildProgress,
                    Delivered = StacksOf(bp.Delivered)
                });
            }

            var stationIds = new List<int>(world.Crafting.OrdersByStation.Keys);
            stationIds.Sort();
            foreach (int stationId in stationIds)
            {
                foreach (var order in world.Crafting.OrdersByStation[stationId])
                {
                    data.CraftOrders.Add(new SavedCraftOrder
                    {
                        Id = order.Id,
                        StationId = stationId,
                        RecipeId = order.RecipeId,
                        Remaining = order.Remaining,
                        MaintainTarget = order.MaintainTarget
                    });
                }
            }

            var alertIds = new List<string>(world.Alerts.Active.Keys);
            alertIds.Sort(StringComparer.Ordinal);
            foreach (string alertId in alertIds)
            {
                var alert = world.Alerts.Active[alertId];
                data.Alerts.Add(new SavedAlert { Id = alert.Id, Severity = (int)alert.Severity, X = alert.X, Y = alert.Y });
            }

            var streamNames = new List<string>(world.StreamsForSave().Keys);
            streamNames.Sort(StringComparer.Ordinal);
            foreach (string name in streamNames)
            {
                var stream = world.StreamsForSave()[name];
                data.RngStreams.Add(new SavedRngStream { Name = name, State = stream.State, Inc = stream.Inc });
            }

            var gasComponents = new List<int>(world.Networks.GasStored.Keys);
            gasComponents.Sort();
            foreach (int component in gasComponents)
            {
                data.GasComponents.Add(new SavedGasComponent
                {
                    ComponentId = component,
                    Stored = world.Networks.GasStored[component]
                });
            }

            var unlockedIds = new List<string>(world.Tech.Unlocked);
            unlockedIds.Sort(StringComparer.Ordinal);
            data.TechUnlocked.AddRange(unlockedIds);
            data.ResearchTarget = world.Tech.ResearchTarget;
            foreach (var entry in world.Tech.PaidCores.SortedEntries())
            {
                data.ResearchPaid.Add(new SavedStack { ItemId = entry.Key, Count = entry.Value });
            }
            foreach (JobType job in Enum.GetValues(typeof(JobType)))
            {
                world.Jobs.Quotas.TryGetValue(job, out int quota);
                data.JobQuotas.Add(new SavedStack { ItemId = job.ToString(), Count = quota });
            }
            for (int j = 0; j < JobSystem.JobCount; j++)
            {
                for (int t = 0; t < JobSystem.TaskTypeCount; t++)
                {
                    data.JobMatrix.Add(world.Jobs.Matrix[j, t]);
                }
            }

            foreach (var unit in world.Battle.UnitsSorted())
            {
                data.CombatUnits.Add(new SavedCombatUnit
                {
                    Id = unit.Id,
                    Side = (int)unit.Side,
                    X = unit.X,
                    Y = unit.Y,
                    Hp = unit.Hp,
                    MaxHp = unit.MaxHp,
                    DamagePerHit = unit.DamagePerHit,
                    Order = (int)unit.Order,
                    HoldX = unit.HoldX,
                    HoldY = unit.HoldY,
                    PatrolAx = unit.PatrolAx,
                    PatrolAy = unit.PatrolAy,
                    PatrolBx = unit.PatrolBx,
                    PatrolBy = unit.PatrolBy,
                    Armored = unit.Armored
                });
            }
            foreach (var wave in world.Battle.PendingWaves)
            {
                data.PendingWaves.Add(new SavedWave { Tick = wave.tick, Count = wave.count });
            }
            data.RallyX = world.Battle.RallyX;
            data.RallyY = world.Battle.RallyY;
            data.ShieldTicksRemaining = world.Battle.ShieldTicksRemaining;
            data.ActiveRaidStrength = world.Battle.ActiveRaidStrength;

            foreach (var bot in world.Bots.AllSorted())
            {
                // Bot carried loads normalize into piles like colonists do.
                if (bot.CarryingCount > 0)
                {
                    data.Piles.Add(new SavedPile
                    {
                        Id = nextPileId,
                        ItemId = bot.CarryingItem,
                        Count = bot.CarryingCount,
                        X = bot.X,
                        Y = bot.Y
                    });
                    nextPileId++;
                }
                data.Bots.Add(new SavedBot
                {
                    Id = bot.Id,
                    HomeStationId = bot.HomeStationId,
                    X = bot.X,
                    Y = bot.Y,
                    Battery = bot.Battery
                });
            }

            return data;
        }

        public static World Restore(SaveData data)
        {
            if (data.SchemaVersion != GameConstants.SaveSchemaVersion)
            {
                throw new InvalidDataException(
                    "Unsupported save schema " + data.SchemaVersion + ", expected " + GameConstants.SaveSchemaVersion +
                    " (run migrations via FromGzipJson)");
            }
            var terrain = TerrainGrid.FromHeights(data.RegionSize, data.TerrainHeights);
            var world = new World(data.Seed, terrain, spawnColony: false);
            world.RestoreTick(data.Tick);
            world.Buildings.RestoreFrom(data.Buildings);
            world.Piles.RestoreFrom(data.Piles);
            world.Nodes.RestoreFrom(data.Nodes);
            world.Blueprints.RestoreFrom(data.Blueprints);
            world.Colonists.RestoreFrom(data.Colonists);
            world.Crafting.RestoreFrom(data.CraftOrders);
            world.Alerts.RestoreFrom(data.Alerts);
            world.Networks.RestoreGas(data.GasComponents);
            world.Bots.RestoreFrom(data.Bots);
            world.Battle.RestoreFrom(data.CombatUnits, data.PendingWaves,
                data.RallyX, data.RallyY, data.ShieldTicksRemaining, data.ActiveRaidStrength);
            world.RestoreMeta(data);
            return world;
        }

        public static string ToCanonicalJson(SaveData data)
        {
            return JsonConvert.SerializeObject(data, Formatting.None);
        }

        public static byte[] ToGzipJson(SaveData data)
        {
            byte[] raw = Encoding.UTF8.GetBytes(ToCanonicalJson(data));
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }
                return output.ToArray();
            }
        }

        public static SaveData FromGzipJson(byte[] bytes)
        {
            using (var input = new MemoryStream(bytes))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
            {
                string json = reader.ReadToEnd();
                var root = JObject.Parse(json);
                SaveMigrations.UpgradeInPlace(root);
                return root.ToObject<SaveData>();
            }
        }

        public static void WriteFile(string path, SaveData data)
        {
            File.WriteAllBytes(path, ToGzipJson(data));
        }

        public static SaveData ReadFile(string path)
        {
            return FromGzipJson(File.ReadAllBytes(path));
        }

        private static List<SavedStack> StacksOf(Inventory inventory)
        {
            var stacks = new List<SavedStack>();
            foreach (var entry in inventory.SortedEntries())
            {
                stacks.Add(new SavedStack { ItemId = entry.Key, Count = entry.Value });
            }
            return stacks;
        }

        private static Dictionary<string, int> SortedCopy(Dictionary<string, int> source)
        {
            var keys = new List<string>(source.Keys);
            keys.Sort(StringComparer.Ordinal);
            var copy = new Dictionary<string, int>();
            foreach (string key in keys)
            {
                copy[key] = source[key];
            }
            return copy;
        }

        private static List<int> SortedKeys(IEnumerable<int> keys)
        {
            var list = new List<int>(keys);
            list.Sort();
            return list;
        }
    }
}
