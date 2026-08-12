namespace Starsoil.Core
{
    /// <summary>FNV-1a 64-bit hashing (strings and accumulated state fingerprints).</summary>
    public static class Fnv1a64
    {
        public const ulong OffsetBasis = 14695981039346656037UL;
        public const ulong Prime = 1099511628211UL;

        public static ulong HashString(string s)
        {
            ulong hash = OffsetBasis;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                hash = (hash ^ (byte)c) * Prime;
                hash = (hash ^ (byte)(c >> 8)) * Prime;
            }
            return hash;
        }
    }

    /// <summary>
    /// Accumulating FNV-1a hasher used for deterministic world-state fingerprints
    /// (determinism tests compare these across replays, see docs/plan/08).
    /// </summary>
    public struct StateHasher
    {
        private ulong _hash;

        public static StateHasher Create()
        {
            StateHasher h;
            h._hash = Fnv1a64.OffsetBasis;
            return h;
        }

        public void Add(byte v)
        {
            _hash = (_hash ^ v) * Fnv1a64.Prime;
        }

        public void Add(ulong v)
        {
            const int bytesPerULong = 8;
            const int bitsPerByte = 8;
            for (int i = 0; i < bytesPerULong; i++)
            {
                Add((byte)(v >> (i * bitsPerByte)));
            }
        }

        public void Add(long v) => Add((ulong)v);

        public void Add(int v) => Add((ulong)(uint)v);

        public void Add(string s)
        {
            Add(Fnv1a64.HashString(s));
        }

        public ulong Value => _hash;
    }
}
