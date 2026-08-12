using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    /// <summary>Load-shedding order when power runs short (docs/plan/03: 生保>物流>生产>娱乐).</summary>
    public enum PowerPriority
    {
        LifeSupport = 0,
        Logistics = 1,
        Production = 2,
        Comfort = 3
    }

    /// <summary>
    /// Power and oxygen networks (docs/plan/03): pylon-graph topology — pylons link to
    /// pylons within link range, buildings attach to any pylon in coverage radius, and
    /// each connected component settles supply/demand once per tick. Oxygen reuses the
    /// same topology with gas pylons; the crash pod tank stays as the standby source and
    /// is flagged as backup once a powered electrolyzer joins a network (M2-T2).
    /// </summary>
    public sealed class NetworkSystem
    {
        private const float TickHours = 1f / GameConstants.TicksPerHour;

        private bool _dirty = true;
        /// <summary>buildingId → power component id (0 = unconnected).</summary>
        private readonly Dictionary<int, int> _powerComponent = new Dictionary<int, int>();
        /// <summary>buildingId → oxygen component id (0 = unconnected).</summary>
        private readonly Dictionary<int, int> _gasComponent = new Dictionary<int, int>();
        /// <summary>power component id → satisfaction per priority class (true = powered).</summary>
        private readonly Dictionary<int, bool[]> _powerSatisfied = new Dictionary<int, bool[]>();
        /// <summary>oxygen component id → stored O2 (sum of tanks, clamped by capacity).</summary>
        public readonly Dictionary<int, float> GasStored = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _gasCapacity = new Dictionary<int, float>();

        public float LastSupplyKw { get; private set; }
        public float LastDemandKw { get; private set; }
        public float BatteryStoredKwh { get; private set; }
        public float BatteryCapacityKwh { get; private set; }
        /// <summary>True once any powered electrolyzer feeds a network (pod becomes backup).</summary>
        public bool PodIsBackup { get; private set; }

        public void MarkDirty()
        {
            _dirty = true;
        }

        public int PowerComponentOf(int buildingId) =>
            _powerComponent.TryGetValue(buildingId, out int c) ? c : 0;

        public int GasComponentOf(int buildingId) =>
            _gasComponent.TryGetValue(buildingId, out int c) ? c : 0;

        /// <summary>Is this building's priority class currently powered?</summary>
        public bool IsPowered(World world, BuildingState building)
        {
            if (!BuildingDefs.TryGet(building.DefId, out var def) || def.PowerKw <= 0f)
            {
                return true;
            }
            int component = PowerComponentOf(building.Id);
            if (component == 0)
            {
                return false;
            }
            return _powerSatisfied.TryGetValue(component, out var classes) && classes[(int)def.Priority];
        }

        public void Tick(World world)
        {
            if (_dirty)
            {
                RebuildTopology(world);
                _dirty = false;
            }
            SettlePower(world);
            SettleGas(world);
        }

        // ---------------------------------------------------------------- topology

        private void RebuildTopology(World world)
        {
            BuildLayer(world, isPower: true, _powerComponent);
            BuildLayer(world, isPower: false, _gasComponent);
        }

        private static void BuildLayer(World world, bool isPower, Dictionary<int, int> componentOf)
        {
            componentOf.Clear();
            var pylons = new List<BuildingState>();
            foreach (var pair in world.Buildings.All)
            {
                if (BuildingDefs.TryGet(pair.Value.DefId, out var def) &&
                    (isPower ? def.IsPowerPylon : def.IsGasPylon))
                {
                    pylons.Add(pair.Value);
                }
            }
            pylons.Sort((a, b) => a.Id.CompareTo(b.Id));

            // Union-find over pylons.
            var parent = new Dictionary<int, int>();
            foreach (var pylon in pylons)
            {
                parent[pylon.Id] = pylon.Id;
            }
            int linkRange = isPower ? Balance.PowerPylonLinkRange : Balance.GasPylonLinkRange;
            for (int i = 0; i < pylons.Count; i++)
            {
                for (int j = i + 1; j < pylons.Count; j++)
                {
                    if (Chebyshev(pylons[i], pylons[j]) <= linkRange)
                    {
                        Union(parent, pylons[i].Id, pylons[j].Id);
                    }
                }
            }

            int coverRadius = isPower ? Balance.PowerPylonCoverRadius : Balance.GasPylonCoverRadius;
            foreach (var pair in world.Buildings.All)
            {
                var building = pair.Value;
                foreach (var pylon in pylons)
                {
                    if (Chebyshev(building, pylon) <= coverRadius)
                    {
                        componentOf[building.Id] = Find(parent, pylon.Id);
                        break;
                    }
                }
            }
        }

        private static int Chebyshev(BuildingState a, BuildingState b)
        {
            return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        private static int Find(Dictionary<int, int> parent, int id)
        {
            while (parent[id] != id)
            {
                parent[id] = parent[parent[id]];
                id = parent[id];
            }
            return id;
        }

        private static void Union(Dictionary<int, int> parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb)
            {
                // Deterministic: smaller id wins as root.
                if (ra < rb)
                {
                    parent[rb] = ra;
                }
                else
                {
                    parent[ra] = rb;
                }
            }
        }

        // ---------------------------------------------------------------- power

        private void SettlePower(World world)
        {
            _powerSatisfied.Clear();
            LastSupplyKw = 0f;
            LastDemandKw = 0f;
            BatteryStoredKwh = 0f;
            BatteryCapacityKwh = 0f;

            var components = new HashSet<int>(_powerComponent.Values);
            var sorted = new List<int>(components);
            sorted.Sort();
            foreach (int component in sorted)
            {
                SettlePowerComponent(world, component);
            }
        }

        private void SettlePowerComponent(World world, int component)
        {
            float supply = 0f;
            float[] demandByClass = new float[Balance.PowerPriorityClassCount];
            var batteries = new List<BuildingState>();

            foreach (var pair in world.Buildings.All)
            {
                if (PowerComponentOf(pair.Key) != component ||
                    !BuildingDefs.TryGet(pair.Value.DefId, out var def))
                {
                    continue;
                }
                if (def.Kind == BuildingKind.SolarPanel)
                {
                    supply += SolarOutput(world);
                }
                else if (def.Kind == BuildingKind.WindTurbine)
                {
                    supply += world.WindFactor * Balance.WindTurbineKw;
                }
                else if (def.Kind == BuildingKind.HandCrank)
                {
                    supply += pair.Value.CrankActive ? Balance.HandCrankKw : 0f;
                }
                else if (def.Kind == BuildingKind.Battery)
                {
                    batteries.Add(pair.Value);
                }
                if (def.PowerKw > 0f && pair.Value.WantsPower)
                {
                    demandByClass[(int)def.Priority] += def.PowerKw;
                }
            }

            batteries.Sort((a, b) => a.Id.CompareTo(b.Id));
            float stored = 0f;
            foreach (var battery in batteries)
            {
                stored += battery.BatteryKwh;
            }

            // Serve classes in priority order; batteries cover deficits, absorb surplus.
            bool[] satisfied = new bool[Balance.PowerPriorityClassCount];
            float remaining = supply;
            float drawnFromBattery = 0f;
            for (int cls = 0; cls < Balance.PowerPriorityClassCount; cls++)
            {
                float need = demandByClass[cls];
                if (need <= 0f)
                {
                    satisfied[cls] = true;
                    continue;
                }
                if (remaining >= need)
                {
                    remaining -= need;
                    satisfied[cls] = true;
                }
                else
                {
                    float deficitKwh = (need - remaining) * TickHours;
                    if (stored - drawnFromBattery >= deficitKwh)
                    {
                        drawnFromBattery += deficitKwh;
                        remaining = 0f;
                        satisfied[cls] = true;
                    }
                    else
                    {
                        satisfied[cls] = false;
                    }
                }
            }

            // Charge batteries with leftover supply.
            float surplusKwh = remaining * TickHours;
            float delta = surplusKwh - drawnFromBattery;
            ApplyBatteryDelta(batteries, delta);

            float totalStored = 0f;
            foreach (var battery in batteries)
            {
                totalStored += battery.BatteryKwh;
            }

            _powerSatisfied[component] = satisfied;
            LastSupplyKw += supply;
            for (int cls = 0; cls < Balance.PowerPriorityClassCount; cls++)
            {
                LastDemandKw += demandByClass[cls];
            }
            BatteryStoredKwh += totalStored;
            BatteryCapacityKwh += batteries.Count * Balance.BatteryCapacityKwh;
        }

        private static void ApplyBatteryDelta(List<BuildingState> batteries, float deltaKwh)
        {
            foreach (var battery in batteries)
            {
                if (deltaKwh > 0f)
                {
                    float room = Balance.BatteryCapacityKwh - battery.BatteryKwh;
                    float add = Math.Min(room, deltaKwh);
                    battery.BatteryKwh += add;
                    deltaKwh -= add;
                }
                else if (deltaKwh < 0f)
                {
                    float take = Math.Min(battery.BatteryKwh, -deltaKwh);
                    battery.BatteryKwh -= take;
                    deltaKwh += take;
                }
                if (Math.Abs(deltaKwh) < 1e-6f)
                {
                    break;
                }
            }
        }

        public static float SolarOutput(World world)
        {
            if (world.IsNight)
            {
                return 0f;
            }
            float dayFraction = (world.HourOfDay - Balance.DayStartHour) /
                                (float)(Balance.NightStartHour - Balance.DayStartHour);
            return Balance.SolarPanelKw * (float)Math.Sin(dayFraction * Math.PI);
        }

        // ---------------------------------------------------------------- oxygen

        private void SettleGas(World world)
        {
            _gasCapacity.Clear();
            var components = new HashSet<int>(_gasComponent.Values);
            var sorted = new List<int>(components);
            sorted.Sort();

            PodIsBackup = false;
            foreach (int component in sorted)
            {
                float capacity = 0f;
                float production = 0f;
                foreach (var pair in world.Buildings.All)
                {
                    if (GasComponentOf(pair.Key) != component ||
                        !BuildingDefs.TryGet(pair.Value.DefId, out var def))
                    {
                        continue;
                    }
                    if (def.Kind == BuildingKind.GasTank)
                    {
                        capacity += Balance.GasTankCapacity;
                    }
                    else if (def.Kind == BuildingKind.Electrolyzer)
                    {
                        capacity += Balance.ElectrolyzerBufferCapacity;
                        if (IsPowered(world, pair.Value) && pair.Value.WantsPower)
                        {
                            production += TryElectrolyze(world, pair.Value);
                        }
                    }
                }
                if (production > 0f)
                {
                    PodIsBackup = true;
                }
                GasStored.TryGetValue(component, out float stored);
                stored = Math.Min(capacity, stored + production);
                GasStored[component] = stored;
                _gasCapacity[component] = capacity;
            }
        }

        /// <summary>Electrolyzers consume water from their buffer; hauls keep it stocked.</summary>
        private static float TryElectrolyze(World world, BuildingState electrolyzer)
        {
            electrolyzer.ProcessAccum += Balance.ElectrolyzerO2PerTick;
            if (electrolyzer.ProcessAccum >= Balance.ElectrolyzerO2PerWater)
            {
                if (electrolyzer.Stock.TryRemove(ItemIds.Water, 1))
                {
                    electrolyzer.ProcessAccum -= Balance.ElectrolyzerO2PerWater;
                    return Balance.ElectrolyzerO2PerWater;
                }
                electrolyzer.ProcessAccum = Balance.ElectrolyzerO2PerWater;
                return 0f;
            }
            return 0f;
        }

        /// <summary>Draws O2 for an indoor colonist: connected network first, pod tank fallback.</summary>
        public bool TryDrawO2ForIndoor(World world, int buildingId, float amount)
        {
            int component = GasComponentOf(buildingId);
            if (component != 0 && GasStored.TryGetValue(component, out float stored) && stored >= amount)
            {
                GasStored[component] = stored - amount;
                return true;
            }
            return world.Life.TryDrawTank(amount);
        }

        /// <summary>Refill source lookup for bottles: charging stations with network O2.</summary>
        public BuildingState FindChargingStation(World world)
        {
            BuildingState best = null;
            foreach (var pair in world.Buildings.All)
            {
                if (!BuildingDefs.TryGet(pair.Value.DefId, out var def) ||
                    def.Kind != BuildingKind.AirChargingStation)
                {
                    continue;
                }
                int component = GasComponentOf(pair.Key);
                if (component != 0 && GasStored.TryGetValue(component, out float stored) &&
                    stored >= Balance.BottleCapacity && (best == null || pair.Key < best.Id))
                {
                    best = pair.Value;
                }
            }
            return best;
        }

        public bool TryDrawFromStationNetwork(World world, BuildingState station, float amount)
        {
            int component = GasComponentOf(station.Id);
            if (component != 0 && GasStored.TryGetValue(component, out float stored) && stored >= amount)
            {
                GasStored[component] = stored - amount;
                return true;
            }
            return false;
        }

        internal void RestoreGas(List<SavedGasComponent> saved)
        {
            GasStored.Clear();
            if (saved == null)
            {
                return;
            }
            foreach (var s in saved)
            {
                GasStored[s.ComponentId] = s.Stored;
            }
        }
    }
}
