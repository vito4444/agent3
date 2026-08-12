using System.Collections.Generic;

namespace Starsoil.Core
{
    public sealed class SavedBuilding
    {
        public int Id;
        public string DefId;
        public int X;
        public int Y;
        public int Rotation;
    }

    /// <summary>
    /// Save schema v0 (docs/plan/08): single-region world — seed, tick, terrain, buildings.
    /// Schema changes bump GameConstants.SaveSchemaVersion and register a migration.
    /// </summary>
    public sealed class SaveData
    {
        public int SchemaVersion = GameConstants.SaveSchemaVersion;
        public ulong Seed;
        public long Tick;
        public int RegionSize;
        public byte[] TerrainHeights;
        public List<SavedBuilding> Buildings = new List<SavedBuilding>();
    }
}
