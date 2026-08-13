using UnityEngine;
using Starsoil.Core;
using Starsoil.Data;
using Starsoil.Presentation;
using Starsoil.UI;

namespace Starsoil.Bootstrap
{
    /// <summary>
    /// Runtime composition root (docs/plan/08: services are assembled here, no global
    /// singletons). The Main scene is intentionally empty; everything M1 needs is built
    /// in code so no hand-authored scene content or prefabs are required yet.
    /// </summary>
    public static class GameBootstrap
    {
        private const ulong DefaultWorldSeed = 42UL;
        private const float CameraFov = 30f;
        private const float CameraFarClip = 3000f;
        private const float SunPitch = 50f;
        private const float SunYaw = -30f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (Object.FindFirstObjectByType<SimDriver>() != null)
            {
                return;
            }

            var settings = GameSettings.Load();
            L10n.TryLoadDefault();
            ItemCatalog.TryLoadDefault();

            // Steam boots before any UI so the interface language can follow the
            // Steam client (docs/plan/10 M8-T1). Harmless without the STEAM define.
            var steamGo = new GameObject("SteamBridge");
            var steam = steamGo.AddComponent<SteamBridge>();
            steam.InitApi();

            // Language priority: QA override > explicit user setting > Steam client
            // language > default zh. Titles/buttons capture text at BuildUi time.
            string demoLanguage = System.Environment.GetEnvironmentVariable("STARSOIL_DEMO_LANG");
            string language = settings.Language;
            if ((string.IsNullOrEmpty(language) || language == "auto") &&
                steam.TryGetLanguage(out string steamLanguage))
            {
                language = steamLanguage;
            }
            L10n.SetLanguage(string.IsNullOrEmpty(demoLanguage) ? language : demoLanguage);

            var universe = Universe.NewGame(DefaultWorldSeed, GameConstants.DefaultRegionSize);
            BodiesData.LoadInto(universe);
            CatalogContent.ApplyTo(universe);
            var world = universe.ActiveWorld;
            if (settings.SkipTutorial)
            {
                world.Commands.Enqueue(new SetTutorialSkippedCommand { Skipped = true });
            }

            var root = new GameObject("Starsoil");

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = CameraFov;
            cam.farClipPlane = CameraFarClip;
            var rig = camGo.AddComponent<CameraRig>();
            rig.RegionSize = world.Terrain.Size;

            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(root.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(SunPitch, SunYaw, 0f);
            var sun = lightGo.AddComponent<SunController>();
            sun.Init(world, light);

            var terrainGo = new GameObject("TerrainView");
            terrainGo.transform.SetParent(root.transform, false);
            var terrainView = terrainGo.AddComponent<TerrainView>();
            terrainView.Build(world.Terrain);

            var viewGo = new GameObject("WorldView");
            viewGo.transform.SetParent(root.transform, false);
            var worldView = viewGo.AddComponent<WorldView>();
            worldView.Init(world, rig);

            var entitiesGo = new GameObject("EntityViews");
            entitiesGo.transform.SetParent(root.transform, false);
            var entities = entitiesGo.AddComponent<EntityViews>();
            entities.Init(world);

            var placementGo = new GameObject("PlacementController");
            placementGo.transform.SetParent(root.transform, false);
            var placement = placementGo.AddComponent<PlacementController>();
            placement.Init(world, rig);

            var hudGo = new GameObject("DebugHud");
            hudGo.transform.SetParent(root.transform, false);
            var hud = hudGo.AddComponent<DebugHud>();

            var hudUiGo = new GameObject("Hud");
            hudUiGo.transform.SetParent(root.transform, false);
            var hudUi = hudUiGo.AddComponent<HudController>();

            var craftPanelGo = new GameObject("CraftPanel");
            craftPanelGo.transform.SetParent(root.transform, false);
            var craftPanel = craftPanelGo.AddComponent<CraftPanelController>();
            craftPanel.Init(world);
            placement.StationClicked += craftPanel.Open;

            var techPanelGo = new GameObject("TechPanel");
            techPanelGo.transform.SetParent(root.transform, false);
            var techPanel = techPanelGo.AddComponent<TechPanelController>();
            techPanel.Init(world);

            var browserGo = new GameObject("RecipeBrowser");
            browserGo.transform.SetParent(root.transform, false);
            var browser = browserGo.AddComponent<RecipeBrowserController>();
            browser.Init(world);

            var jobsPanelGo = new GameObject("JobsPanel");
            jobsPanelGo.transform.SetParent(root.transform, false);
            var jobsPanel = jobsPanelGo.AddComponent<JobsPanelController>();
            jobsPanel.Init(world);

            var driverGo = new GameObject("SimDriver");
            driverGo.transform.SetParent(root.transform, false);
            var driver = driverGo.AddComponent<SimDriver>();
            driver.Init(world, settings, rig, worldView, entities, terrainView, placement, hud, hudUi, craftPanel, sun);
            driver.RegisterPanels(techPanel, jobsPanel);
            driver.RegisterBrowser(browser);
            driver.AttachUniverse(universe);
            steam.AttachUniverse(universe);
            steamGo.transform.SetParent(root.transform, false);

            var starMapGo = new GameObject("StarMap");
            starMapGo.transform.SetParent(root.transform, false);
            var starMap = starMapGo.AddComponent<StarMapController>();
            starMap.Init(universe, driver.SwitchRegion);

            var tradeGo = new GameObject("TradePanel");
            tradeGo.transform.SetParent(root.transform, false);
            var tradePanel = tradeGo.AddComponent<TradePanelController>();
            tradePanel.Init(universe);

            hud.Init(world, rig, worldView, () => driver.Speed);
            hudUi.Init(world, () => driver.Speed, driver.JumpCameraTo, driver.NewGame, driver.SkipTutorial);

            DemoScreenshotDriver.InstallIfRequested(root, universe);

            Debug.Log("[GameBootstrap] World ready: seed " + world.Seed + ", region " + world.Terrain.Size +
                      ", colonists " + world.Colonists.AliveCount);
        }
    }
}
