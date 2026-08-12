using System;

namespace Starsoil.Core
{
    /// <summary>
    /// Deterministic PCG32 generator. All sim randomness must come from named streams
    /// derived from the world seed; System.Random and UnityEngine.Random are banned in
    /// Game.Core (docs/plan/08 determinism policy, enforced by scripts/check_core_constraints.sh).
    /// Stream state is persisted in saves so loading never re-rolls history.
    /// </summary>
    public sealed class Rng
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong SeedMix = 0x9E3779B97F4A7C15UL;
        private const float UIntToUnitFloat = 1.0f / 4294967296.0f;

        private ulong _state;
        private ulong _inc;

        public Rng(ulong seed, ulong sequence)
        {
            _inc = (sequence << 1) | 1UL;
            _state = 0UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        /// <summary>Derives an independent stream from the world seed and a stable stream name.</summary>
        public static Rng CreateStream(ulong worldSeed, string streamName)
        {
            ulong nameHash = Fnv1a64.HashString(streamName);
            return new Rng(worldSeed ^ (nameHash * SeedMix), nameHash);
        }

        /// <summary>Internal state, exposed for save persistence (docs/plan/08).</summary>
        public ulong State => _state;

        public ulong Inc => _inc;

        /// <summary>Rebuilds a stream from persisted state.</summary>
        public static Rng FromState(ulong state, ulong inc)
        {
            var rng = new Rng(0UL, 0UL);
            rng._state = state;
            rng._inc = inc;
            return rng;
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = old * Multiplier + _inc;
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentException("maxExclusive must be greater than minInclusive");
            }
            ulong range = (ulong)((long)maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat()
        {
            return NextUInt() * UIntToUnitFloat;
        }
    }
}
