using UnityEngine;
using Starsoil.Core;
using Starsoil.Presentation;

namespace Starsoil.Bootstrap
{
    /// <summary>
    /// Runtime composition root (docs/plan/08: services are assembled here, no global
    /// singletons). The Main scene is intentionally empty; everything M0 needs is built
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

            var world = new World(DefaultWorldSeed, GameConstants.DefaultRegionSize);

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

            var terrainGo = new GameObject("TerrainView");
            terrainGo.transform.SetParent(root.transform, false);
            var terrainView = terrainGo.AddComponent<TerrainView>();
            terrainView.Build(world.Terrain);

            var viewGo = new GameObject("WorldView");
            viewGo.transform.SetParent(root.transform, false);
            var worldView = viewGo.AddComponent<WorldView>();
            worldView.Init(world, rig);

            var placementGo = new GameObject("PlacementController");
            placementGo.transform.SetParent(root.transform, false);
            var placement = placementGo.AddComponent<PlacementController>();
            placement.Init(world, rig);

            var hudGo = new GameObject("DebugHud");
            hudGo.transform.SetParent(root.transform, false);
            var hud = hudGo.AddComponent<DebugHud>();

            var driverGo = new GameObject("SimDriver");
            driverGo.transform.SetParent(root.transform, false);
            var driver = driverGo.AddComponent<SimDriver>();
            driver.Init(world, worldView, terrainView, placement, hud);
            hud.Init(world, rig, worldView, () => driver.Speed);

            Debug.Log("[GameBootstrap] World ready: seed " + world.Seed + ", region " + world.Terrain.Size);
        }
    }
}
