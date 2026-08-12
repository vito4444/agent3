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
