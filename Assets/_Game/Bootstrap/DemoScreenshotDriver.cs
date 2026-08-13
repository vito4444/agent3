using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Starsoil.UI;

namespace Starsoil.Bootstrap
{
    /// <summary>
    /// Headless visual-QA driver: with STARSOIL_DEMO_SHOTS=&lt;dir&gt; set, walks a fixed
    /// schedule (overview, then each panel), captures a screenshot per step into that
    /// directory and quits. Runs under xvfb on the VM so UI layout can be reviewed
    /// and iterated on without a desktop. No-op in normal play (env var absent).
    /// </summary>
    public sealed class DemoScreenshotDriver : MonoBehaviour
    {
        private const float StartDelaySeconds = 4f;
        private const float StepSeconds = 1.5f;

        private string _outDir;
        private readonly List<(string name, System.Action open, System.Action close)> _steps =
            new List<(string, System.Action, System.Action)>();
        private int _index = -1;
        private float _timer;
        private bool _pendingShot;

        public static void InstallIfRequested(GameObject host)
        {
            string dir = System.Environment.GetEnvironmentVariable("STARSOIL_DEMO_SHOTS");
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }
            var driver = host.AddComponent<DemoScreenshotDriver>();
            driver._outDir = dir;
        }

        private void Start()
        {
            Directory.CreateDirectory(_outDir);
            var tech = Object.FindFirstObjectByType<TechPanelController>();
            var jobs = Object.FindFirstObjectByType<JobsPanelController>();
            var browser = Object.FindFirstObjectByType<RecipeBrowserController>();
            var starMap = Object.FindFirstObjectByType<StarMapController>();
            var trade = Object.FindFirstObjectByType<TradePanelController>();

            _steps.Add(("01_overview", null, null));
            if (tech != null)
            {
                _steps.Add(("02_tech", tech.Toggle, tech.Toggle));
            }
            if (jobs != null)
            {
                _steps.Add(("03_jobs", jobs.Toggle, jobs.Toggle));
            }
            if (browser != null)
            {
                _steps.Add(("04_recipes", browser.Toggle, browser.Toggle));
            }
            if (starMap != null)
            {
                _steps.Add(("05_starmap", starMap.Toggle, starMap.Toggle));
            }
            if (trade != null)
            {
                _steps.Add(("06_trade", trade.Toggle, trade.Toggle));
            }
            _timer = StartDelaySeconds;
        }

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f)
            {
                return;
            }
            if (_pendingShot)
            {
                // One frame after opening the panel so UI Toolkit has laid out.
                ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, _steps[_index].name + ".png"));
                _pendingShot = false;
                _timer = StepSeconds * 0.5f;
                return;
            }
            // Close the previous step, advance, open the next.
            if (_index >= 0 && _steps[_index].close != null)
            {
                _steps[_index].close();
            }
            _index++;
            if (_index >= _steps.Count)
            {
                Debug.Log("[DemoShots] complete: " + _outDir);
                Application.Quit();
                return;
            }
            _steps[_index].open?.Invoke();
            _pendingShot = true;
            _timer = StepSeconds;
        }
    }
}
