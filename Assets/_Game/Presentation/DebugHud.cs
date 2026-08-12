using UnityEngine;
using Starsoil.Core;

namespace Starsoil.Presentation
{
    /// <summary>
    /// Developer overlay for M0 smoke checks (tick, time, speed, camera height, controls).
    /// This is not the game UI; the UI Toolkit shell arrives with later milestones.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        private const float PanelWidth = 460f;
        private const float PanelHeight = 190f;

        private World _world;
        private CameraRig _rig;
        private WorldView _view;
        private System.Func<int> _speedGetter;

        public void Init(World world, CameraRig rig, WorldView view, System.Func<int> speedGetter)
        {
            _world = world;
            _rig = rig;
            _view = view;
            _speedGetter = speedGetter;
        }

        public void SwitchWorld(World world)
        {
            _world = world;
        }

        private void OnGUI()
        {
            if (_world == null)
            {
                return;
            }
            GUILayout.BeginArea(new Rect(8f, 8f, PanelWidth, PanelHeight), GUI.skin.box);
            GUILayout.Label("Starsoil M0 | tick " + _world.Tick +
                            " | day " + _world.Day + " hour " + _world.HourOfDay +
                            " | speed x" + _speedGetter() +
                            " | cam " + Mathf.RoundToInt(_rig.Height) + "m (" + (_rig.IsFarBand ? "L2" : "L1") + ")" +
                            " | buildings " + _view.BuildingCount);
            GUILayout.Label("Move WASD/MMB · Rotate Q/E/RMB · Zoom wheel (12-900m, LOD @300m)");
            GUILayout.Label("Place LMB · Rotate blueprint R · Remove Delete");
            GUILayout.Label("Pause Space · Speed F1/F2/F3 · Save F5 · Load F9");
            GUILayout.EndArea();
        }
    }
}
