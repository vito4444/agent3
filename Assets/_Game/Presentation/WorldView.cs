using System.Collections.Generic;
using UnityEngine;
using Starsoil.Core;

namespace Starsoil.Presentation
{
    /// <summary>
    /// Syncs sim building state to grey-box view objects via sim events (docs/plan/08:
    /// presentation subscribes, never polls sim internals). Each building has a full L1
    /// model and a flat L2 block; CameraRig's far band toggles between them (M0-T5).
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        private const float FullModelHeight = 3f;
        private const float BlockModelHeight = 1f;

        private static readonly Color FullColor = new Color(0.30f, 0.69f, 0.31f);
        private static readonly Color BlockColor = new Color(0.55f, 0.58f, 0.60f);

        private readonly Dictionary<int, GameObject> _views = new Dictionary<int, GameObject>();
        private World _world;
        private CameraRig _rig;
        private Material _fullMaterial;
        private Material _blockMaterial;
        private bool _far;

        public int BuildingCount => _views.Count;

        public void Init(World world, CameraRig rig)
        {
            _world = world;
            _rig = rig;
            _fullMaterial = MakeMaterial(FullColor);
            _blockMaterial = MakeMaterial(BlockColor);
            rig.FarBandChanged += OnFarBandChanged;
            RebuildAll();
        }

        public void SwitchWorld(World world)
        {
            _world = world;
            RebuildAll();
        }

        /// <summary>Consume events produced by the tick that just ran.</summary>
        public void ApplyEvents(List<ISimEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                switch (events[i])
                {
                    case BuildingPlacedEvent placed:
                        CreateView(placed.BuildingId);
                        break;
                    case BuildingRemovedEvent removed:
                        if (_views.TryGetValue(removed.BuildingId, out var go))
                        {
                            Destroy(go);
                            _views.Remove(removed.BuildingId);
                        }
                        break;
                }
            }
        }

        private void OnFarBandChanged(bool far)
        {
            _far = far;
            foreach (var pair in _views)
            {
                ApplyLod(pair.Value);
            }
        }

        private void RebuildAll()
        {
            foreach (var pair in _views)
            {
                Destroy(pair.Value);
            }
            _views.Clear();
            foreach (var pair in _world.Buildings.All)
            {
                CreateView(pair.Key);
            }
        }

        private void CreateView(int buildingId)
        {
            if (!_world.Buildings.TryGet(buildingId, out var state) ||
                !BuildingDefs.TryGet(state.DefId, out var def))
            {
                return;
            }
            BuildingSystem.FootprintSize(def, state.Rotation, out int w, out int h);
            float ground = _world.Terrain.GetHeight(state.X, state.Y) * GameConstants.MetersPerTerrainStep;

            var root = new GameObject("Building_" + buildingId);
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(state.X + w * 0.5f, ground, state.Y + h * 0.5f);

            var full = GameObject.CreatePrimitive(PrimitiveType.Cube);
            full.name = "Full";
            full.transform.SetParent(root.transform, false);
            full.transform.localScale = new Vector3(w, FullModelHeight, h);
            full.transform.localPosition = new Vector3(0f, FullModelHeight * 0.5f, 0f);
            full.GetComponent<MeshRenderer>().sharedMaterial = _fullMaterial;
            Destroy(full.GetComponent<Collider>());

            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Block";
            block.transform.SetParent(root.transform, false);
            block.transform.localScale = new Vector3(w, BlockModelHeight, h);
            block.transform.localPosition = new Vector3(0f, BlockModelHeight * 0.5f, 0f);
            block.GetComponent<MeshRenderer>().sharedMaterial = _blockMaterial;
            Destroy(block.GetComponent<Collider>());

            _views.Add(buildingId, root);
            ApplyLod(root);
        }

        private void ApplyLod(GameObject root)
        {
            root.transform.Find("Full").gameObject.SetActive(!_far);
            root.transform.Find("Block").gameObject.SetActive(_far);
        }

        private static Material MakeMaterial(Color color)
        {
            var material = MaterialLib.NewLit();
            material.color = color;
            return material;
        }
    }
}
