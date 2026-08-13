using UnityEngine;

namespace Starsoil.Presentation
{
    /// <summary>
    /// Runtime material factory. Player builds only contain shaders referenced by
    /// assets, so Shader.Find alone breaks outside the editor: the base materials
    /// created by SetupProject under Resources/ carry the URP shaders into the build.
    /// Shader.Find stays as the editor/test fallback (works before SetupProject ran).
    /// </summary>
    public static class MaterialLib
    {
        private const string LitBase = "StarsoilLitBase";
        private const string UnlitBase = "StarsoilUnlitBase";

        public static Material NewLit()
        {
            return New(LitBase, "Universal Render Pipeline/Lit", "Standard");
        }

        public static Material NewUnlit()
        {
            return New(UnlitBase, "Universal Render Pipeline/Unlit", "Unlit/Color");
        }

        private static Material New(string resourceName, string shaderName, string fallbackShaderName)
        {
            var baseMaterial = Resources.Load<Material>(resourceName);
            if (baseMaterial != null)
            {
                return new Material(baseMaterial);
            }
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                shader = Shader.Find(fallbackShaderName);
            }
            if (shader == null)
            {
                // Last resort: keep the game alive (magenta), never throw.
                Debug.LogError("[MaterialLib] no shader available for " + resourceName +
                               "; run Starsoil/Setup Project to create base materials.");
                shader = Shader.Find("Hidden/InternalErrorShader");
            }
            return new Material(shader);
        }
    }
}
