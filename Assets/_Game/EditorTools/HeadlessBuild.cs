using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Starsoil.EditorTools
{
    /// <summary>
    /// Batchmode build entry points for headless environments
    /// (scripts/unity_headless.sh and any non-game-ci automation).
    /// Usage: Unity -batchmode -quit -executeMethod Starsoil.EditorTools.HeadlessBuild.Linux64
    /// Output goes to Builds/&lt;platform&gt;/ under the project root.
    /// </summary>
    public static class HeadlessBuild
    {
        /// <summary>Must match SetupProject.ScenePath (created on first setup).</summary>
        private const string ScenePath = "Assets/_Game/Bootstrap/Main.unity";

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
            if (!File.Exists(ScenePath))
            {
                // Fresh VM/checkout: run the first-open automation to create the
                // URP assets, PanelSettings and the main scene before building.
                SetupProject.Run();
            }
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
                return;
            }

            // Dev-build data contract: DataFiles resolves <build>/../data relative to
            // Application.dataPath, so ship the repo data next to the player binary.
            string buildDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            string repoRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            CopyDirectory(Path.Combine(repoRoot, "data"), Path.Combine(buildDir, "data"));
            CopyDirectory(Path.Combine(repoRoot, "GeneratedData"), Path.Combine(buildDir, "GeneratedData"));
        }

        private static void CopyDirectory(string from, string to)
        {
            if (!Directory.Exists(from))
            {
                return;
            }
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
            }
        }
    }
}
