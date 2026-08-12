using System.Collections.Generic;

namespace Starsoil.Core
{
    /// <summary>
    /// Lazy faction base instantiation (docs/plan/07, M6-T3): nine layout templates
    /// (3 personalities × 3 scales) stamped into a region world only when the player
    /// actually arrives. The layout seed is hash(universe seed, factionId, bodyId), so
    /// revisits regenerate identically; destroyed buildings persist through the normal
    /// frozen-region snapshot (M6-T4 differential persistence for free).
    /// </summary>
    public static class FactionBases
    {
        private const int LayoutRowY = 10;

        /// <summary>Scale 1..3 from the faction's base tier (T growth).</summary>
        public static int ScaleFor(Faction faction)
        {
            return System.Math.Max(1, System.Math.Min(3, faction.BaseTier));
        }

        public static World Instantiate(ulong universeSeed, Faction faction, BodyDef body, int regionSize)
        {
            ulong seed = Fnv1a64.HashString(universeSeed + ":" + faction.Id + ":" + body.Id);
            var world = World.CreateLandingRegion(seed, regionSize, body, 0, new List<Ingredient>());
            int scale = ScaleFor(faction);
            var stream = world.GetStream("faction_base");
            int cx = world.StartX;
            int cy = world.StartY;

            // Shared spine: pad-like clearing marked by roads.
            for (int i = -2; i <= 2; i++)
            {
                world.Buildings.Place(BuildingDefs.RoadId, cx + i, cy + 4, 0, out _);
            }

            switch (faction.Personality)
            {
                case FactionPersonality.Merchant:
                    // 货场: storage yards + beacon; the home template always carries a
                    // shield dome (vassal treaty precondition, docs/plan/07 附庸).
                    world.Buildings.Place(BuildingDefs.ShieldDomeId, cx + 4, cy + 8, 0, out _);
                    for (int i = 0; i < 2 + scale * 2; i++)
                    {
                        world.Buildings.Place(BuildingDefs.SmallStorageId,
                            cx - 6 + (i % 3) * 3, cy + 6 + (i / 3) * 3, 0, out _);
                    }
                    world.Buildings.Place(BuildingDefs.LandingBeaconId, cx + 6, cy + 5, 0, out _);
                    break;

                case FactionPersonality.Expansionist:
                    // 兵营: wall ring + ammo/works blocks (grey-box as test blocks).
                    for (int i = -4; i <= 4; i += 2)
                    {
                        world.Buildings.Place(BuildingDefs.TestBlockId, cx + i, cy + 7, 0, out _);
                    }
                    for (int i = 0; i < scale * 2; i++)
                    {
                        world.Buildings.Place(BuildingDefs.HandCrankId, cx - 4 + i * 2, cy + LayoutRowY, 0, out _);
                    }
                    break;

                default:
                    // 晶塔: battery clusters + pylons, dense and tall.
                    for (int i = 0; i < 2 + scale * 2; i++)
                    {
                        world.Buildings.Place(BuildingDefs.BatteryId,
                            cx - 3 + (i % 3) * 2, cy + 6 + (i / 3) * 2, 0, out _);
                    }
                    world.Buildings.Place(BuildingDefs.PowerPylonId, cx, cy + LayoutRowY, 0, out _);
                    world.Buildings.Place(BuildingDefs.PowerPylonId, cx + 2, cy + LayoutRowY, 0, out _);
                    break;
            }
            _ = stream;
            return world;
        }

        /// <summary>Distinct signature per personality (M6-T3 acceptance helper): the
        /// building-kind histogram differs between the three templates.</summary>
        public static string Signature(World world)
        {
            var counts = new SortedDictionary<string, int>();
            foreach (var pair in world.Buildings.All)
            {
                counts.TryGetValue(pair.Value.DefId, out int count);
                counts[pair.Value.DefId] = count + 1;
            }
            var parts = new List<string>();
            foreach (var pair in counts)
            {
                parts.Add(pair.Key + ":" + pair.Value);
            }
            return string.Join(",", parts);
        }
    }
}
