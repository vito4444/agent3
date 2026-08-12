using UnityEngine;
using Starsoil.Core;

namespace Starsoil.Presentation
{
    /// <summary>
    /// Mouse building placement (M0-T4): ghost preview snapped to the grid, green when
    /// legal and red when not, LMB places, R rotates, Delete removes the hovered building.
    /// All mutations go through the command queue; this class never touches sim state
    /// directly (verified by the M0-T4 acceptance).
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
        private bool _hasCell;
        private int _cellX;
        private int _cellY;

        public void Init(World world, CameraRig rig)
        {
            _world = world;
            _rig = rig;

            _ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ghost.name = "PlacementGhost";
            Destroy(_ghost.GetComponent<Collider>());
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            _ghostMaterial = new Material(shader);
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

            if (Input.GetKeyDown(KeyCode.R))
            {
                _rotation = (_rotation + 1) % 4;
            }

            Ray ray = _rig.Cam.ScreenPointToRay(Input.mousePosition);
            _hasCell = GridPicker.TryPickCell(_world.Terrain, ray, out _cellX, out _cellY);
            if (!_hasCell)
            {
                _ghost.SetActive(false);
                return;
            }

            BuildingDefs.TryGet(BuildingDefs.TestBlockId, out var def);
            BuildingSystem.FootprintSize(def, _rotation, out int w, out int h);
            float ground = _world.Terrain.GetHeight(_cellX, _cellY) * GameConstants.MetersPerTerrainStep;

            _ghost.SetActive(true);
            _ghost.transform.position = new Vector3(_cellX + w * 0.5f, ground + GhostHeight * 0.5f, _cellY + h * 0.5f);
            _ghost.transform.localScale = new Vector3(w, GhostHeight, h);

            PlacementError error = _world.Buildings.CanPlace(BuildingDefs.TestBlockId, _cellX, _cellY, _rotation);
            _ghostMaterial.color = error == PlacementError.None ? GhostValid : GhostInvalid;

            if (Input.GetMouseButtonDown(0))
            {
                _world.Commands.Enqueue(new PlaceBuildingCommand
                {
                    DefId = BuildingDefs.TestBlockId,
                    X = _cellX,
                    Y = _cellY,
                    Rotation = _rotation
                });
            }

            if (Input.GetKeyDown(KeyCode.Delete))
            {
                int hovered = _world.Buildings.GetBuildingAt(_cellX, _cellY);
                if (hovered != 0)
                {
                    _world.Commands.Enqueue(new RemoveBuildingCommand { BuildingId = hovered });
                }
            }
        }
    }
}
