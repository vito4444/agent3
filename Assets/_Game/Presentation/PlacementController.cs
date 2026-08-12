using System;
using System.Collections.Generic;
using UnityEngine;
using Starsoil.Core;

namespace Starsoil.Presentation
{
    /// <summary>
    /// Mouse interaction (M1): build mode places construction blueprints (hauled and
    /// built by colonists); inspect mode toggles node harvesting, opens station craft
    /// panels, toggles crank staffing; Delete demolishes (50% refund) or cancels
    /// blueprints. All mutations go through the command queue.
    /// </summary>
    public sealed class PlacementController : MonoBehaviour
    {
        private static readonly Color GhostValid = new Color(0.35f, 0.9f, 0.4f, 1f);
        private static readonly Color GhostInvalid = new Color(0.95f, 0.3f, 0.25f, 1f);
        private const float GhostHeight = 3f;

        private World _world;
        private CameraRig _rig;
        private GameObject _ghost;
        private Material _ghostMaterial;
        private int _rotation;
        private bool _buildMode;
        private int _selectedIndex;
        private bool _hasCell;
        private int _cellX;
        private int _cellY;

        /// <summary>Raised when the player clicks a craft station (UI opens the orders panel).</summary>
        public event Action<int> StationClicked;

        public bool BuildMode => _buildMode;

        /// <summary>Buildable list = full menu filtered by unlocked tech (M2-T11 gating).</summary>
        private List<string> UnlockedBuildables()
        {
            var list = new List<string>();
            foreach (string defId in BuildingDefs.BuildableAll)
            {
                if (BuildingDefs.TryGet(defId, out var def) && _world.Tech.IsBuildingUnlocked(def))
                {
                    list.Add(defId);
                }
            }
            return list;
        }

        public string SelectedDefId
        {
            get
            {
                var unlocked = UnlockedBuildables();
                if (unlocked.Count == 0)
                {
                    return BuildingDefs.BuildableT0[0];
                }
                return unlocked[Math.Min(_selectedIndex, unlocked.Count - 1)];
            }
        }

        public void Init(World world, CameraRig rig)
        {
            _world = world;
            _rig = rig;

            _ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ghost.name = "PlacementGhost";
            _ghost.SetActive(false);
            Destroy(_ghost.GetComponent<Collider>());
            _ghostMaterial = Mats.Unlit(GhostValid);
            _ghost.GetComponent<MeshRenderer>().sharedMaterial = _ghostMaterial;
        }

        public void SwitchWorld(World world)
        {
            _world = world;
        }

        private void Update()
        {
            if (_world == null || _rig == null || _rig.Cam == null)
            {
                return;
            }

            HandleModeKeys();

            Ray ray = _rig.Cam.ScreenPointToRay(Input.mousePosition);
            _hasCell = GridPicker.TryPickCell(_world.Terrain, ray, out _cellX, out _cellY);
            if (!_hasCell)
            {
                _ghost.SetActive(false);
                return;
            }

            if (_buildMode)
            {
                TickBuildMode();
            }
            else
            {
                _ghost.SetActive(false);
                TickInspectMode();
            }

            if (Input.GetKeyDown(KeyCode.Delete))
            {
                int blueprintId = _world.Blueprints.GetBlueprintAt(_cellX, _cellY);
                if (blueprintId != 0)
                {
                    _world.Commands.Enqueue(new CancelBlueprintCommand { BlueprintId = blueprintId });
                    return;
                }
                int buildingId = _world.Buildings.GetBuildingAt(_cellX, _cellY);
                if (buildingId != 0)
                {
                    _world.Commands.Enqueue(new DemolishBuildingCommand { BuildingId = buildingId });
                }
            }
        }

        private void HandleModeKeys()
        {
            if (Input.GetKeyDown(KeyCode.B))
            {
                _buildMode = !_buildMode;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _buildMode = false;
            }
            if (_buildMode)
            {
                int menuSize = UnlockedBuildables().Count;
                for (int i = 0; i < 9 && i < menuSize; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    {
                        _selectedIndex = i;
                    }
                }
                // [ and ] page through the unlocked list beyond the digit keys.
                if (Input.GetKeyDown(KeyCode.LeftBracket) && menuSize > 0)
                {
                    _selectedIndex = (_selectedIndex + menuSize - 1) % menuSize;
                }
                if (Input.GetKeyDown(KeyCode.RightBracket) && menuSize > 0)
                {
                    _selectedIndex = (_selectedIndex + 1) % menuSize;
                }
                if (Input.GetKeyDown(KeyCode.R))
                {
                    _rotation = (_rotation + 1) % 4;
                }
            }
        }

        private void TickBuildMode()
        {
            string defId = SelectedDefId;
            BuildingDefs.TryGet(defId, out var def);
            BuildingSystem.FootprintSize(def, _rotation, out int w, out int h);
            float ground = _world.Terrain.GetHeight(_cellX, _cellY) * GameConstants.MetersPerTerrainStep;

            _ghost.SetActive(true);
            _ghost.transform.position = new Vector3(_cellX + w * 0.5f, ground + GhostHeight * 0.5f, _cellY + h * 0.5f);
            _ghost.transform.localScale = new Vector3(w, GhostHeight, h);

            PlacementError error = _world.Blueprints.CanPlace(defId, _cellX, _cellY, _rotation);
            _ghostMaterial.color = error == PlacementError.None ? GhostValid : GhostInvalid;

            if (Input.GetMouseButtonDown(0))
            {
                _world.Commands.Enqueue(new PlaceBlueprintCommand
                {
                    DefId = defId,
                    X = _cellX,
                    Y = _cellY,
                    Rotation = _rotation
                });
            }
        }

        private void TickInspectMode()
        {
            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            int nodeId = _world.Nodes.GetNodeAt(_cellX, _cellY);
            if (nodeId != 0 && _world.Nodes.TryGet(nodeId, out var node))
            {
                _world.Commands.Enqueue(new ToggleNodeDesignationCommand
                {
                    NodeId = nodeId,
                    Designated = !node.Designated
                });
                return;
            }

            int buildingId = _world.Buildings.GetBuildingAt(_cellX, _cellY);
            if (buildingId != 0 && _world.Buildings.TryGet(buildingId, out var building) &&
                BuildingDefs.TryGet(building.DefId, out var def))
            {
                if (def.IsStation)
                {
                    StationClicked?.Invoke(buildingId);
                }
                else if (def.Kind == BuildingKind.HandCrank)
                {
                    _world.Commands.Enqueue(new SetCrankStaffedCommand
                    {
                        BuildingId = buildingId,
                        Staffed = !building.StaffedRequested
                    });
                }
            }
        }
    }
}
