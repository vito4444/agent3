using System;
using System.Collections.Generic;

namespace Starsoil.Core
{
    /// <summary>
    /// Grid A* (docs/plan/08): 8-directional, height steps of more than 1 block movement,
    /// non-walkable buildings block, roads reduce cost by the road speed factor, and
    /// diagonals must not cut corners. HPA* clustering arrives when maps exceed 160².
    /// </summary>
    public static class Pathfinding
    {
        public const int StraightCost = 10;
        public const int DiagonalCost = 14;
        private const int MaxExpandedNodes = 40000;
        private const int MaxHeightStep = 1;

        public delegate bool WalkableQuery(int x, int y);

        public sealed class Context
        {
            public TerrainGrid Terrain;
            public BuildingSystem Buildings;

            public bool IsWalkable(int x, int y)
            {
                return Terrain.InBounds(x, y) && Buildings.IsCellWalkable(x, y);
            }

            public bool CanStep(int fromX, int fromY, int toX, int toY)
            {
                if (!IsWalkable(toX, toY))
                {
                    return false;
                }
                if (Math.Abs(Terrain.GetHeight(fromX, fromY) - Terrain.GetHeight(toX, toY)) > MaxHeightStep)
                {
                    return false;
                }
                int dx = toX - fromX;
                int dy = toY - fromY;
                if (dx != 0 && dy != 0)
                {
                    // No corner cutting: both orthogonal cells must be steppable.
                    if (!CanStepOrthogonal(fromX, fromY, fromX + dx, fromY) ||
                        !CanStepOrthogonal(fromX, fromY, fromX, fromY + dy))
                    {
                        return false;
                    }
                }
                return true;
            }

            private bool CanStepOrthogonal(int fromX, int fromY, int toX, int toY)
            {
                return IsWalkable(toX, toY) &&
                       Math.Abs(Terrain.GetHeight(fromX, fromY) - Terrain.GetHeight(toX, toY)) <= MaxHeightStep;
            }

            public int StepCost(int fromX, int fromY, int toX, int toY)
            {
                int cost = (fromX != toX && fromY != toY) ? DiagonalCost : StraightCost;
                if (Buildings.IsCellRoad(fromX, fromY) && Buildings.IsCellRoad(toX, toY))
                {
                    cost = (int)(cost / Balance.RoadSpeedFactor);
                }
                return cost;
            }
        }

        private static readonly (int dx, int dy)[] Neighbors =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)
        };

        /// <summary>Finds a path from start to goal (inclusive). Returns null when unreachable.</summary>
        public static List<(int x, int y)> FindPath(Context ctx, int startX, int startY, int goalX, int goalY)
        {
            if (startX == goalX && startY == goalY)
            {
                return new List<(int, int)> { (startX, startY) };
            }
            if (!ctx.IsWalkable(goalX, goalY) || !ctx.Terrain.InBounds(startX, startY))
            {
                return null;
            }

            int size = ctx.Terrain.Size;
            var gScore = new Dictionary<int, int>();
            var cameFrom = new Dictionary<int, int>();
            var open = new BinaryHeap();

            int startKey = startY * size + startX;
            int goalKey = goalY * size + goalX;
            gScore[startKey] = 0;
            open.Push(startKey, Heuristic(startX, startY, goalX, goalY));

            int expanded = 0;
            while (open.Count > 0 && expanded < MaxExpandedNodes)
            {
                int currentKey = open.Pop();
                if (currentKey == goalKey)
                {
                    return Reconstruct(cameFrom, currentKey, startKey, size);
                }
                expanded++;
                int cx = currentKey % size;
                int cy = currentKey / size;
                int currentG = gScore[currentKey];

                foreach (var (dx, dy) in Neighbors)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (!ctx.CanStep(cx, cy, nx, ny))
                    {
                        continue;
                    }
                    int neighborKey = ny * size + nx;
                    int tentative = currentG + ctx.StepCost(cx, cy, nx, ny);
                    if (gScore.TryGetValue(neighborKey, out int existing) && existing <= tentative)
                    {
                        continue;
                    }
                    gScore[neighborKey] = tentative;
                    cameFrom[neighborKey] = currentKey;
                    open.Push(neighborKey, tentative + Heuristic(nx, ny, goalX, goalY));
                }
            }
            return null;
        }

        /// <summary>
        /// Octile-distance heuristic. Roads make true costs cheaper than plain octile
        /// distance, so the estimate is divided by the road factor to stay admissible.
        /// </summary>
        private static int Heuristic(int x, int y, int gx, int gy)
        {
            int dx = Math.Abs(x - gx);
            int dy = Math.Abs(y - gy);
            int straight = Math.Max(dx, dy) - Math.Min(dx, dy);
            int diagonal = Math.Min(dx, dy);
            int estimate = straight * StraightCost + diagonal * DiagonalCost;
            // Roads can reduce true cost by RoadSpeedFactor; shrink the estimate so it
            // stays admissible on road networks.
            return (int)(estimate / Balance.RoadSpeedFactor);
        }

        private static List<(int x, int y)> Reconstruct(Dictionary<int, int> cameFrom, int endKey, int startKey, int size)
        {
            var path = new List<(int, int)>();
            int key = endKey;
            while (true)
            {
                path.Add((key % size, key / size));
                if (key == startKey)
                {
                    break;
                }
                key = cameFrom[key];
            }
            path.Reverse();
            return path;
        }

        /// <summary>Min-heap keyed by f-score with stable tie-breaking on insertion order
        /// (determinism requirement, docs/plan/08).</summary>
        private sealed class BinaryHeap
        {
            private readonly List<(int key, int f, long order)> _items = new List<(int, int, long)>();
            private long _counter;

            public int Count => _items.Count;

            public void Push(int key, int f)
            {
                _items.Add((key, f, _counter));
                _counter++;
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (Less(_items[i], _items[parent]))
                    {
                        (_items[i], _items[parent]) = (_items[parent], _items[i]);
                        i = parent;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public int Pop()
            {
                var top = _items[0];
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1;
                    int right = i * 2 + 2;
                    int smallest = i;
                    if (left < _items.Count && Less(_items[left], _items[smallest]))
                    {
                        smallest = left;
                    }
                    if (right < _items.Count && Less(_items[right], _items[smallest]))
                    {
                        smallest = right;
                    }
                    if (smallest == i)
                    {
                        break;
                    }
                    (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
                    i = smallest;
                }
                return top.key;
            }

            private static bool Less((int key, int f, long order) a, (int key, int f, long order) b)
            {
                if (a.f != b.f)
                {
                    return a.f < b.f;
                }
                return a.order < b.order;
            }
        }
    }
}
