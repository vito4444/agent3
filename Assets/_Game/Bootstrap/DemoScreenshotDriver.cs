using System.IO;
using UnityEngine;
using Starsoil.Core;
using Starsoil.UI;

namespace Starsoil.Bootstrap
{
    /// <summary>
    /// Headless visual-QA driver. With STARSOIL_DEMO_SHOTS=&lt;dir&gt; set, arranges the
    /// state named by STARSOIL_DEMO_PANEL, waits for layout, captures one screenshot
    /// and quits. One state per process: switching panels inside one run left stale
    /// panel textures on software renderers, so scripts/demo_shots.sh loops instead.
    ///
    /// States: hud | tech | jobs | recipes | starmap | trade (static panels),
    /// tech_researching | jobs_assigned | trade_quotes | craft (interaction states),
    /// world3d (keeps the 3D camera; expects a tiny resolution and a long timeout —
    /// software rasterizers need minutes per URP frame).
    /// </summary>
    public sealed class DemoScreenshotDriver : MonoBehaviour
    {
        private const float WarmupSeconds = 5f;
        private const float QuitDelaySeconds = 2f;
        /// <summary>Frame-based schedule for world3d, where seconds are meaningless.</summary>
        private const int WarmupFrames3D = 3;
        private const string ResearchDemoNode = "power_basics";

        private string _outDir;
        private string _panel;
        private Universe _universe;
        private float _timer;
        private int _frames;
        private bool _use3D;
        private bool _captured;

        public static void InstallIfRequested(GameObject host, Universe universe)
        {
            string dir = System.Environment.GetEnvironmentVariable("STARSOIL_DEMO_SHOTS");
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }
            // Xvfb has no window manager: the player never gets focus and would pause
            // its main loop entirely (0% CPU) unless allowed to run unfocused.
            Application.runInBackground = true;
            var driver = host.AddComponent<DemoScreenshotDriver>();
            driver._outDir = dir;
            driver._universe = universe;
            driver._panel = System.Environment.GetEnvironmentVariable("STARSOIL_DEMO_PANEL") ?? "hud";
            driver._use3D = driver._panel == "world3d";
        }

        private void Start()
        {
            Directory.CreateDirectory(_outDir);
            // Software-rasterizer relief (llvmpipe under xvfb): no vsync, no shadows;
            // outside world3d the scene camera is disabled entirely — UI Toolkit
            // renders independently of cameras and URP costs minutes per frame here.
            QualitySettings.vSyncCount = 0;
            QualitySettings.shadows = ShadowQuality.Disable;
            var camera = Camera.main;
            if (camera != null && !_use3D)
            {
                camera.enabled = false;
            }

            var world = _universe.ActiveWorld;
            switch (_panel)
            {
                case "tech":
                    Object.FindFirstObjectByType<TechPanelController>()?.Toggle();
                    break;
                case "tech_researching":
                    world.Commands.Enqueue(new SetResearchTargetCommand { NodeId = ResearchDemoNode });
                    StepCommandsThrough();
                    Object.FindFirstObjectByType<TechPanelController>()?.Toggle();
                    break;
                case "jobs":
                    Object.FindFirstObjectByType<JobsPanelController>()?.Toggle();
                    break;
                case "jobs_assigned":
                    var quotas = new SetJobQuotasCommand();
                    quotas.Quotas.Add(new System.Collections.Generic.KeyValuePair<string, int>("Miner", 2));
                    quotas.Quotas.Add(new System.Collections.Generic.KeyValuePair<string, int>("Builder", 1));
                    world.Commands.Enqueue(quotas);
                    StepCommandsThrough();
                    Object.FindFirstObjectByType<JobsPanelController>()?.Toggle();
                    break;
                case "recipes":
                    Object.FindFirstObjectByType<RecipeBrowserController>()?.Toggle();
                    break;
                case "starmap":
                    Object.FindFirstObjectByType<StarMapController>()?.Toggle();
                    break;
                case "trade":
                    Object.FindFirstObjectByType<TradePanelController>()?.Toggle();
                    break;
                case "trade_quotes":
                    // Comms online, then fast-forward past a 12h quote refresh so the
                    // board is populated when the panel opens.
                    _universe.FactionLayerVisible = true;
                    for (int i = 0; i < 13 * GameConstants.TicksPerHour; i++)
                    {
                        _universe.Step();
                    }
                    Object.FindFirstObjectByType<TradePanelController>()?.Toggle();
                    break;
                case "craft":
                    int stationId = world.Buildings.Place(BuildingDefs.WorkbenchId,
                        world.StartX + 3, world.StartY + 3, 0, out _);
                    Object.FindFirstObjectByType<CraftPanelController>()?.Open(stationId);
                    break;
            }
            Debug.Log("[DemoShots] panel=" + _panel);
            _timer = WarmupSeconds;
        }

        private void Update()
        {
            _frames++;
            if (_use3D)
            {
                // Seconds-based waits are useless at minutes-per-frame: count frames.
                if (!_captured && _frames >= WarmupFrames3D)
                {
                    Capture();
                }
                else if (_captured && _frames >= WarmupFrames3D + 2)
                {
                    Finish();
                }
                return;
            }
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f)
            {
                return;
            }
            if (!_captured)
            {
                Capture();
                _timer = QuitDelaySeconds;
                return;
            }
            Finish();
        }

        /// <summary>Queued commands only execute on a sim step; panels rebuild on open,
        /// so digest the queue before toggling or the shot shows stale state.</summary>
        private void StepCommandsThrough()
        {
            for (int i = 0; i < 5; i++)
            {
                _universe.Step();
            }
        }

        private void Capture()
        {
            Debug.Log("[DemoShots] capture " + _panel);
            ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, _panel + ".png"));
            _captured = true;
        }

        private void Finish()
        {
            Debug.Log("[DemoShots] complete " + _panel);
            Application.Quit();
        }
    }
}
