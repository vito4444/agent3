using System.IO;
using UnityEngine;
using Starsoil.Core;
using Starsoil.Data;
using Starsoil.Presentation;
using Starsoil.UI;

namespace Starsoil.Bootstrap
{
    /// <summary>
    /// Fixed-step tick driver (docs/plan/02: 10 ticks per real second at 1×; speeds 0/1/2/4).
    /// Forwards per-tick sim events to the views, owns save/load hotkeys, auto-pause on
    /// critical alerts (setting, docs/plan/02 anti-death-spiral) and the new-game flow.
    /// </summary>
    public sealed class SimDriver : MonoBehaviour
    {
        private const int SpeedPaused = 0;
        private const int Speed1 = 1;
        private const int Speed2 = 2;
        private const int Speed4 = 4;
        private const int MaxTicksPerFrame = 40;
        private const string SaveFileName = "save_v1.json.gz";

        private World _world;
        private WorldView _view;
        private EntityViews _entities;
        private TerrainView _terrainView;
        private PlacementController _placement;
        private DebugHud _hud;
        private HudController _hudUi;
        private CraftPanelController _craftPanel;
        private SunController _sun;
        private CameraRig _rig;
        private GameSettings _settings;

        private float _accumulator;
        private int _speed = Speed1;

        public int Speed => _speed;
        public World World => _world;
        public GameSettings Settings => _settings;

        private TechPanelController _techPanel;
        private JobsPanelController _jobsPanel;

        private RecipeBrowserController _browser;

        public void RegisterPanels(TechPanelController techPanel, JobsPanelController jobsPanel)
        {
            _techPanel = techPanel;
            _jobsPanel = jobsPanel;
        }

        public void RegisterBrowser(RecipeBrowserController browser)
        {
            _browser = browser;
        }

        public void Init(World world, GameSettings settings, CameraRig rig, WorldView view, EntityViews entities,
            TerrainView terrainView, PlacementController placement, DebugHud hud,
            HudController hudUi, CraftPanelController craftPanel, SunController sun)
        {
            _world = world;
            _settings = settings;
            _rig = rig;
            _view = view;
            _entities = entities;
            _terrainView = terrainView;
            _placement = placement;
            _hud = hud;
            _hudUi = hudUi;
            _craftPanel = craftPanel;
            _sun = sun;
        }

        public void JumpCameraTo(int x, int y)
        {
            float ground = _world.Terrain.GetHeight(x, y) * GameConstants.MetersPerTerrainStep;
            _rig.CenterOn(new Vector3(x, ground, y));
        }

        public void SkipTutorial()
        {
            _settings.SkipTutorial = true;
            _settings.Save();
            _world.Commands.Enqueue(new SetTutorialSkippedCommand { Skipped = true });
        }

        public void NewGame()
        {
            // Presentation may use nondeterministic seeds; only Game.Core is banned from them.
            ulong seed = (ulong)System.DateTime.UtcNow.Ticks;
            var world = new World(seed, GameConstants.DefaultRegionSize);
            TempRecipes.LoadInto(world);
            TechTreeData.LoadInto(world);
            if (_settings.SkipTutorial)
            {
                world.Commands.Enqueue(new SetTutorialSkippedCommand { Skipped = true });
            }
            SwitchWorld(world);
            _speed = Speed1;
            Debug.Log("[SimDriver] New game, seed " + seed);
        }

        private void SwitchWorld(World world)
        {
            _world = world;
            _terrainView.Build(world.Terrain);
            _view.SwitchWorld(world);
            _entities.SwitchWorld(world);
            _placement.SwitchWorld(world);
            _hud.SwitchWorld(world);
            _hudUi.SwitchWorld(world);
            _craftPanel.SwitchWorld(world);
            _sun.SwitchWorld(world);
            if (_techPanel != null)
            {
                _techPanel.SwitchWorld(world);
            }
            if (_jobsPanel != null)
            {
                _jobsPanel.SwitchWorld(world);
            }
            if (_browser != null)
            {
                _browser.SwitchWorld(world);
            }
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
                HandleAutoPause();
                ticksThisFrame++;
            }
            if (_accumulator >= 1f)
            {
                // Catch-up cap reached: drop the surplus so the sim slows instead of spiraling.
                _accumulator = 0f;
            }
        }

        private void HandleAutoPause()
        {
            if (!_settings.AutoPauseOnCritical || _speed == SpeedPaused)
            {
                return;
            }
            foreach (var evt in _world.Events)
            {
                if (evt is AlertRaisedEvent raised && raised.Severity == AlertSeverity.Critical)
                {
                    _speed = SpeedPaused;
                    Debug.Log("[SimDriver] Auto-paused on critical alert: " + raised.AlertId);
                    return;
                }
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
                var world = SaveSerializer.Restore(SaveSerializer.ReadFile(path));
                TempRecipes.LoadInto(world);
                TechTreeData.LoadInto(world);
                SwitchWorld(world);
                Debug.Log("[SimDriver] Loaded from " + path + " (tick " + world.Tick + ")");
            }
        }
    }
}
