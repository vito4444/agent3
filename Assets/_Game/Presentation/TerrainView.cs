using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Starsoil.Core;

namespace Starsoil.Presentation
{
    /// <summary>
    /// Grey-box terrain renderer: chunked meshes of flat-topped height steps with skirt
    /// walls, colored by height via a per-cell texture. Visual style tokens arrive with
    /// the M8 art pass; M0 only needs readable steps (docs/plan/05 grey-box-first rule).
    /// </summary>
    public sealed class TerrainView : MonoBehaviour
    {
        private const int ChunkSize = 32;

        private static readonly Color[] HeightPalette =
        {
            new Color(0.43f, 0.36f, 0.28f),
            new Color(0.52f, 0.42f, 0.30f),
            new Color(0.62f, 0.50f, 0.34f),
            new Color(0.70f, 0.57f, 0.38f),
            new Color(0.76f, 0.64f, 0.44f),
            new Color(0.82f, 0.71f, 0.52f),
            new Color(0.88f, 0.79f, 0.62f)
        };

        private readonly List<GameObject> _chunks = new List<GameObject>();
        private Material _material;

        public void Build(TerrainGrid grid)
        {
            Clear();
            _material = CreateMaterial(grid);
            for (int cy = 0; cy < grid.Size; cy += ChunkSize)
            {
                for (int cx = 0; cx < grid.Size; cx += ChunkSize)
                {
                    _chunks.Add(BuildChunk(grid, cx, cy));
                }
            }
        }

        private void Clear()
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                Destroy(_chunks[i]);
            }
            _chunks.Clear();
        }

        private Material CreateMaterial(TerrainGrid grid)
        {
            var tex = new Texture2D(grid.Size, grid.Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < grid.Size; y++)
            {
                for (int x = 0; x < grid.Size; x++)
                {
                    tex.SetPixel(x, y, HeightPalette[grid.GetHeight(x, y)]);
                }
            }
            tex.Apply(false, false);

            var mat = MaterialLib.NewLit();
            mat.mainTexture = tex;
            return mat;
        }

        private GameObject BuildChunk(TerrainGrid grid, int originX, int originY)
        {
            var go = new GameObject("TerrainChunk_" + originX + "_" + originY);
            go.transform.SetParent(transform, false);

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            int maxX = Mathf.Min(originX + ChunkSize, grid.Size);
            int maxY = Mathf.Min(originY + ChunkSize, grid.Size);

            for (int y = originY; y < maxY; y++)
            {
                for (int x = originX; x < maxX; x++)
                {
                    float top = grid.GetHeight(x, y) * GameConstants.MetersPerTerrainStep;
                    Vector2 uv = new Vector2((x + 0.5f) / grid.Size, (y + 0.5f) / grid.Size);
                    AddQuad(vertices, uvs, triangles,
                        new Vector3(x, top, y),
                        new Vector3(x, top, y + 1),
                        new Vector3(x + 1, top, y + 1),
                        new Vector3(x + 1, top, y),
                        uv);

                    AddSkirt(grid, vertices, uvs, triangles, x, y, x + 1, y, uv, isXEdge: true);
                    AddSkirt(grid, vertices, uvs, triangles, x, y, x, y + 1, uv, isXEdge: false);
                }
            }

            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _material;
            return go;
        }

        /// <summary>Adds a vertical wall between a cell and its +x or +y neighbor when heights differ.</summary>
        private static void AddSkirt(TerrainGrid grid, List<Vector3> v, List<Vector2> uv, List<int> t,
            int x, int y, int nx, int ny, Vector2 cellUv, bool isXEdge)
        {
            if (!grid.InBounds(nx, ny))
            {
                return;
            }
            float a = grid.GetHeight(x, y) * GameConstants.MetersPerTerrainStep;
            float b = grid.GetHeight(nx, ny) * GameConstants.MetersPerTerrainStep;
            if (Mathf.Approximately(a, b))
            {
                return;
            }

            float hi = Mathf.Max(a, b);
            float lo = Mathf.Min(a, b);
            if (isXEdge)
            {
                float wx = x + 1;
                bool leftHigher = a > b;
                Vector3 p0 = new Vector3(wx, hi, leftHigher ? y : y + 1);
                Vector3 p1 = new Vector3(wx, hi, leftHigher ? y + 1 : y);
                Vector3 p2 = new Vector3(wx, lo, leftHigher ? y + 1 : y);
                Vector3 p3 = new Vector3(wx, lo, leftHigher ? y : y + 1);
                AddQuad(v, uv, t, p0, p1, p2, p3, cellUv);
            }
            else
            {
                float wz = y + 1;
                bool bottomHigher = a > b;
                Vector3 p0 = new Vector3(bottomHigher ? x + 1 : x, hi, wz);
                Vector3 p1 = new Vector3(bottomHigher ? x : x + 1, hi, wz);
                Vector3 p2 = new Vector3(bottomHigher ? x : x + 1, lo, wz);
                Vector3 p3 = new Vector3(bottomHigher ? x + 1 : x, lo, wz);
                AddQuad(v, uv, t, p0, p1, p2, p3, cellUv);
            }
        }

        private static void AddQuad(List<Vector3> v, List<Vector2> uv, List<int> t,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 cellUv)
        {
            int start = v.Count;
            v.Add(a);
            v.Add(b);
            v.Add(c);
            v.Add(d);
            for (int i = 0; i < 4; i++)
            {
                uv.Add(cellUv);
            }
            t.Add(start);
            t.Add(start + 1);
            t.Add(start + 2);
            t.Add(start);
            t.Add(start + 2);
            t.Add(start + 3);
        }
    }
}
