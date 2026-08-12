using System.Collections.Generic;

namespace Starsoil.Core
{
    /// <summary>
    /// Root simulation aggregate for one region (multi-region arrives at M4).
    /// Step() advances exactly one tick: drain commands, then subsystem ticks
    /// (none yet at M0), then advance the tick counter.
    /// </summary>
    public sealed class World
    {
        public ulong Seed { get; }
        public long Tick { get; private set; }
        public TerrainGrid Terrain { get; }
        public BuildingSystem Buildings { get; }
        public CommandQueue Commands { get; }

        /// <summary>Events raised during the most recent tick; cleared at the start of each tick.</summary>
        public List<ISimEvent> Events { get; } = new List<ISimEvent>();

        public World(ulong seed, int regionSize)
            : this(seed, TerrainGenerator.Generate(regionSize, seed))
        {
        }

        internal World(ulong seed, TerrainGrid terrain)
        {
            Seed = seed;
            Terrain = terrain;
            Buildings = new BuildingSystem(terrain);
            Commands = new CommandQueue();
        }

        public void Step()
        {
            Events.Clear();
            Commands.Drain(this);
            Tick++;
        }

        public long Day => Tick / GameConstants.TicksPerDay;

        public int HourOfDay => (int)(Tick / GameConstants.TicksPerHour % GameConstants.HoursPerDay);

        internal void RestoreTick(long tick)
        {
            Tick = tick;
        }

        /// <summary>Deterministic fingerprint of the full sim state (determinism tests, docs/plan/08).</summary>
        public ulong ComputeStateHash()
        {
            var hasher = StateHasher.Create();
            hasher.Add(Seed);
            hasher.Add(Tick);
            Terrain.WriteTo(ref hasher);
            Buildings.WriteTo(ref hasher);
            return hasher.Value;
        }
    }
}
