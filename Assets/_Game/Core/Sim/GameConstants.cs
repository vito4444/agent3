namespace Starsoil.Core
{
    /// <summary>
    /// Central time/world constants. Values follow docs/plan/02 (time system).
    /// Per docs/plan/08 the sim code must not contain loose numeric literals;
    /// tunables live here or in per-file consts.
    /// </summary>
    public static class GameConstants
    {
        public const int TicksPerHour = 500;
        public const int HoursPerDay = 24;
        public const int TicksPerDay = TicksPerHour * HoursPerDay;
        public const int TicksPerRealSecondAt1x = 10;

        public const int DefaultRegionSize = 192;

        public const int MinTerrainStep = 0;
        public const int MaxTerrainStep = 6;
        public const float MetersPerTerrainStep = 2f;

        public const int SaveSchemaVersion = 3;
    }
}
