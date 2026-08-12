using UnityEngine;
using Starsoil.Core;

namespace Starsoil.Presentation
{
    /// <summary>
    /// Math-based mouse picking against the height-step terrain (no physics colliders):
    /// intersects the ray with each step's top plane from high to low and accepts the
    /// first cell whose height matches that plane.
    /// </summary>
    public static class GridPicker
    {
        public static bool TryPickCell(TerrainGrid grid, Ray ray, out int cellX, out int cellY)
        {
            for (int h = GameConstants.MaxTerrainStep; h >= GameConstants.MinTerrainStep; h--)
            {
                float planeY = h * GameConstants.MetersPerTerrainStep;
                if (Mathf.Approximately(ray.direction.y, 0f))
                {
                    continue;
                }
                float t = (planeY - ray.origin.y) / ray.direction.y;
                if (t < 0f)
                {
                    continue;
                }
                Vector3 p = ray.origin + ray.direction * t;
                int x = Mathf.FloorToInt(p.x);
                int y = Mathf.FloorToInt(p.z);
                if (grid.InBounds(x, y) && grid.GetHeight(x, y) == h)
                {
                    cellX = x;
                    cellY = y;
                    return true;
                }
            }
            cellX = 0;
            cellY = 0;
            return false;
        }
    }
}
