using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Batchmode build entry points for headless environments
    /// (scripts/unity_headless.sh and any non-game-ci automation).
    /// Usage: Unity -batchmode -quit -executeMethod Game.EditorTools.HeadlessBuild.Linux64
    /// Output goes to Builds/&lt;platform&gt;/ under the project root.
    /// </summary>
    public static class HeadlessBuild
    {
        private const string ScenePath = "Assets/_Game/Scenes/Main.unity";

        public static void Linux64()
        {
            Build(BuildTarget.StandaloneLinux64, "Builds/Linux64/Starsoil.x86_64");
        }

        public static void Win64()
        {
            Build(BuildTarget.StandaloneWindows64, "Builds/Win64/Starsoil.exe");
        }

        private static void Build(BuildTarget target, string outputPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                // Non-zero exit so shell callers and CI see the failure.
                EditorApplication.Exit(1);
            }
        }
    }
}
