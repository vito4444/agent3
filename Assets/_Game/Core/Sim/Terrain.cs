using System;

namespace Starsoil.Core
{
    /// <summary>
    /// Square height-step grid (docs/plan/03). Heights are integer steps
    /// 0..GameConstants.MaxTerrainStep, each MetersPerTerrainStep tall.
    /// Buildings require flat, same-height footprints.
    /// </summary>
    public sealed class TerrainGrid
    {
        public int Size { get; }
        private readonly byte[] _heights;

        public TerrainGrid(int size)
        {
            if (size <= 0)
            {
                throw new ArgumentException("size must be positive");
            }
            Size = size;
            _heights = new byte[size * size];
        }

        public static TerrainGrid FromHeights(int size, byte[] heights)
        {
            var grid = new TerrainGrid(size);
            if (heights == null || heights.Length != size * size)
            {
                throw new ArgumentException("heights length does not match size");
            }
            Array.Copy(heights, grid._heights, heights.Length);
            return grid;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size;

        public int GetHeight(int x, int y) => _heights[y * Size + x];

        public void SetHeight(int x, int y, int height)
        {
            if (height < GameConstants.MinTerrainStep || height > GameConstants.MaxTerrainStep)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }
            _heights[y * Size + x] = (byte)height;
        }

        /// <summary>Copy of the raw height array, for serialization.</summary>
        public byte[] CopyHeights()
        {
            var copy = new byte[_heights.Length];
            Array.Copy(_heights, copy, _heights.Length);
            return copy;
        }

        public void WriteTo(ref StateHasher hasher)
        {
            hasher.Add(Size);
            for (int i = 0; i < _heights.Length; i++)
            {
                hasher.Add(_heights[i]);
            }
        }
    }

    /// <summary>
    /// Seeded terrain generation: two-octave value noise quantized to height steps,
    /// with a forced-flat start zone in the region center so early building placement
    /// always has room (docs/plan/02 start-region guarantees; refined in M1).
    /// </summary>
    public static class TerrainGenerator
    {
        // Tuning constants; adjustable within the plan's ±50% balance window.
        private const float CoarseFrequency = 0.035f;
        private const float FineFrequency = 0.11f;
        private const float FineWeight = 0.35f;
        private const int FlatZoneRadius = 24;
        private const int FlatZoneHeight = 2;
        private const int LatticeHashShift = 40;
        private const float LatticeHashScale = 1.0f / (1 << 24);
        private const int UIntBits = 32;

        public static TerrainGrid Generate(int size, ulong worldSeed)
        {
            var grid = new TerrainGrid(size);
            var stream = Rng.CreateStream(worldSeed, "terrain");
            ulong latticeSeed = ((ulong)stream.NextUInt() << UIntBits) | stream.NextUInt();

            int stepCount = GameConstants.MaxTerrainStep - GameConstants.MinTerrainStep + 1;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float coarse = ValueNoise(x * CoarseFrequency, y * CoarseFrequency, latticeSeed);
                    float fine = ValueNoise(x * FineFrequency, y * FineFrequency, latticeSeed ^ Fnv1a64.Prime);
                    float n = coarse * (1f - FineWeight) + fine * FineWeight;
                    int h = GameConstants.MinTerrainStep + (int)(n * stepCount);
                    if (h > GameConstants.MaxTerrainStep)
                    {
                        h = GameConstants.MaxTerrainStep;
                    }
                    grid.SetHeight(x, y, h);
                }
            }

            int center = size / 2;
            for (int y = center - FlatZoneRadius; y <= center + FlatZoneRadius; y++)
            {
                for (int x = center - FlatZoneRadius; x <= center + FlatZoneRadius; x++)
                {
                    if (grid.InBounds(x, y))
                    {
                        grid.SetHeight(x, y, FlatZoneHeight);
                    }
                }
            }
            return grid;
        }

        private static float ValueNoise(float x, float y, ulong seed)
        {
            int x0 = Floor(x);
            int y0 = Floor(y);
            float tx = SmoothStep(x - x0);
            float ty = SmoothStep(y - y0);

            float v00 = LatticeRandom(x0, y0, seed);
            float v10 = LatticeRandom(x0 + 1, y0, seed);
            float v01 = LatticeRandom(x0, y0 + 1, seed);
            float v11 = LatticeRandom(x0 + 1, y0 + 1, seed);

            float a = v00 + (v10 - v00) * tx;
            float b = v01 + (v11 - v01) * tx;
            return a + (b - a) * ty;
        }

        private static float LatticeRandom(int x, int y, ulong seed)
        {
            var hasher = StateHasher.Create();
            hasher.Add(seed);
            hasher.Add(x);
            hasher.Add(y);
            uint reduced = (uint)(hasher.Value >> LatticeHashShift);
            return reduced * LatticeHashScale;
        }

        private static int Floor(float v)
        {
            int i = (int)v;
            return v < i ? i - 1 : i;
        }

        private static float SmoothStep(float t) => t * t * (3f - 2f * t);
    }
}
