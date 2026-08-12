// UnityEditor surface stubs (see UnityEngineStubs.cs header).
// ReSharper disable all
#pragma warning disable
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor
{
    public class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
    }

    public static class AssetDatabase
    {
        public static bool IsValidFolder(string path) => false;
        public static string CreateFolder(string parentFolder, string newFolderName) => "";
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static void AddObjectToAsset(UnityEngine.Object objectToAdd, UnityEngine.Object assetObject) { }
        public static void SaveAssets() { }
        public static void Refresh() { }
        public static T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object => null;
    }

    public static class PlayerSettings
    {
        public static ColorSpace colorSpace { get; set; }
        public static string productName { get; set; }
    }

    public static class QualitySettings
    {
        public static RenderPipelineAsset renderPipeline { get; set; }
    }

    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enabled) { }
    }

    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes { get; set; }
    }
}

namespace UnityEditor
{
    public enum BuildTarget { StandaloneWindows64, StandaloneLinux64, StandaloneOSX }

    [Flags]
    public enum BuildOptions { None = 0, Development = 1 }

    public struct BuildPlayerOptions
    {
        public string[] scenes;
        public string locationPathName;
        public BuildTarget target;
        public BuildOptions options;
    }

    public static class BuildPipeline
    {
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options) => null;
    }

    public static class EditorApplication
    {
        public static void Exit(int returnValue) { }
    }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown, Succeeded, Failed, Cancelled }

    public struct BuildSummary
    {
        public BuildResult result;
    }

    public class BuildReport
    {
        public BuildSummary summary => new BuildSummary();
    }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
    public enum NewSceneMode { Single, Additive }

    public struct Scene
    {
    }

    public static class EditorSceneManager
    {
        public static Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => new Scene();
        public static bool SaveScene(Scene scene, string dstScenePath) => true;
    }
}
