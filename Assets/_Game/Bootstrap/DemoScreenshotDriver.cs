using System.IO;
using UnityEngine;
using Starsoil.UI;

namespace Starsoil.Bootstrap
{
    /// <summary>
    /// Headless visual-QA driver. With STARSOIL_DEMO_SHOTS=&lt;dir&gt; set, opens the single
    /// panel named by STARSOIL_DEMO_PANEL (hud|tech|jobs|recipes|starmap|trade), waits
    /// for layout, captures one screenshot into the directory and quits. One panel per
    /// process: panel switching inside one run left stale panel textures on software
    /// renderers, so scripts/demo_shots.sh loops the process instead. No-op without the
    /// env var.
    /// </summary>
    public sealed class DemoScreenshotDriver : MonoBehaviour
    {
        private const float WarmupSeconds = 5f;
        private const float QuitDelaySeconds = 2f;

        private string _outDir;
        private string _panel;
        private float _timer;
        private bool _captured;

        public static void InstallIfRequested(GameObject host)
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
            driver._panel = System.Environment.GetEnvironmentVariable("STARSOIL_DEMO_PANEL") ?? "hud";
        }

        private void Start()
        {
            Directory.CreateDirectory(_outDir);
            // Software-rasterizer relief (llvmpipe under xvfb): no vsync, no shadows,
            // no 3D camera at all — UI Toolkit renders independently of scene cameras.
            QualitySettings.vSyncCount = 0;
            QualitySettings.shadows = ShadowQuality.Disable;
            var camera = Camera.main;
            if (camera != null)
            {
                camera.enabled = false;
            }

            switch (_panel)
            {
                case "tech":
                    Object.FindFirstObjectByType<TechPanelController>()?.Toggle();
                    break;
                case "jobs":
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
            }
            Debug.Log("[DemoShots] panel=" + _panel);
            _timer = WarmupSeconds;
        }

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f)
            {
                return;
            }
            if (!_captured)
            {
                Debug.Log("[DemoShots] capture " + _panel);
                ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, _panel + ".png"));
                _captured = true;
                _timer = QuitDelaySeconds;
                return;
            }
            Debug.Log("[DemoShots] complete " + _panel);
            Application.Quit();
        }
    }
}
