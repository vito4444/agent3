using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Starsoil.EditorTools
{
    /// <summary>
    /// One-shot project setup (see SETUP.md): creates and assigns the URP pipeline assets,
    /// switches to Linear color space, sets the product name, and registers the empty
    /// bootstrap scene. Exists so the cloud-scaffolded repo needs zero hand-made Unity
    /// assets — Unity itself generates them locally, avoiding hand-written YAML.
    /// </summary>
    public static class SetupProject
    {
        private const string SettingsFolderParent = "Assets";
        private const string SettingsFolderName = "Settings";
        private const string RendererAssetPath = "Assets/Settings/URP_Renderer.asset";
        private const string PipelineAssetPath = "Assets/Settings/URP_Pipeline.asset";
        private const string ResourcesFolder = "Assets/Resources";
        private const string PanelSettingsPath = "Assets/Resources/StarsoilPanelSettings.asset";
        private const string ScenePath = "Assets/_Game/Bootstrap/Main.unity";
        private const string ProductName = "Starsoil";

        [MenuItem("Starsoil/Setup Project (Run Once)")]
        public static void Run()
        {
            if (!AssetDatabase.IsValidFolder(SettingsFolderParent + "/" + SettingsFolderName))
            {
                AssetDatabase.CreateFolder(SettingsFolderParent, SettingsFolderName);
            }

            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, RendererAssetPath);

            var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipeline, PipelineAssetPath);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.productName = ProductName;

            // UI Toolkit panel for the runtime HUD (loaded via Resources by Game.UI).
            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            {
                AssetDatabase.CreateFolder(SettingsFolderParent, "Resources");
            }
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            AssetDatabase.SaveAssets();
            Debug.Log("[Starsoil] Setup complete: URP assigned, Linear color space, product name set, Main scene registered.");
        }
    }
}
