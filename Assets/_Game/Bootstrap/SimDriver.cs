using System.IO;
using UnityEngine;
using Starsoil.Core;
using Starsoil.Presentation;

namespace Starsoil.Bootstrap
{
    /// <summary>
    /// Fixed-step tick driver (docs/plan/02: 10 ticks per real second at 1×; speeds 0/1/2/4).
    /// Forwards per-tick sim events to the views and owns the M0 save/load hotkeys.
    /// </summary>
    public sealed class SimDriver : MonoBehaviour
    {
        private const int SpeedPaused = 0;
        private const int Speed1 = 1;
        private const int Speed2 = 2;
        private const int Speed4 = 4;
        private const int MaxTicksPerFrame = 40;
        private const string SaveFileName = "save_v0.json.gz";

        private World _world;
        private WorldView _view;
        private TerrainView _terrainView;
        private PlacementController _placement;
        private DebugHud _hud;

        private float _accumulator;
        private int _speed = Speed1;

        public int Speed => _speed;
        public World World => _world;

        public void Init(World world, WorldView view, TerrainView terrainView, PlacementController placement, DebugHud hud)
        {
            _world = world;
            _view = view;
            _terrainView = terrainView;
            _placement = placement;
            _hud = hud;
        }

        private void Update()
        {
            HandleSpeedKeys();
            HandleSaveLoadKeys();

            _accumulator += Time.unscaledDeltaTime * _speed * GameConstants.TicksPerRealSecondAt1x;
            int ticksThisFrame = 0;
            while (_accumulator >= 1f && ticksThisFrame < MaxTicksPerFrame)
            {
                _accumulator -= 1f;
                _world.Step();
                _view.ApplyEvents(_world.Events);
                ticksThisFrame++;
            }
            if (_accumulator >= 1f)
            {
                // Catch-up cap reached: drop the surplus so the sim slows instead of spiraling.
                _accumulator = 0f;
            }
        }

        private void HandleSpeedKeys()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _speed = _speed == SpeedPaused ? Speed1 : SpeedPaused;
            }
            if (Input.GetKeyDown(KeyCode.F1))
            {
                _speed = Speed1;
            }
            if (Input.GetKeyDown(KeyCode.F2))
            {
                _speed = Speed2;
            }
            if (Input.GetKeyDown(KeyCode.F3))
            {
                _speed = Speed4;
            }
        }

        private void HandleSaveLoadKeys()
        {
            string path = Path.Combine(Application.persistentDataPath, SaveFileName);
            if (Input.GetKeyDown(KeyCode.F5))
            {
                SaveSerializer.WriteFile(path, SaveSerializer.Capture(_world));
                Debug.Log("[SimDriver] Saved to " + path);
            }
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (!File.Exists(path))
                {
                    Debug.LogWarning("[SimDriver] No save file at " + path);
                    return;
                }
                _world = SaveSerializer.Restore(SaveSerializer.ReadFile(path));
                _terrainView.Build(_world.Terrain);
                _view.SwitchWorld(_world);
                _placement.SwitchWorld(_world);
                _hud.SwitchWorld(_world);
                Debug.Log("[SimDriver] Loaded from " + path + " (tick " + _world.Tick + ")");
            }
        }
    }
}
