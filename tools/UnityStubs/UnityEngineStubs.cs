// Reference-surface stubs of the Unity APIs used by Starsoil's presentation layer.
// They exist ONLY so `dotnet build` can type-check Assets/_Game/** without a Unity
// install (tools/UnityCompileCheck, CI gate 1.5). No runtime behavior — every body
// throws or no-ops. Keep additions minimal and matching real Unity signatures.
// ReSharper disable all
#pragma warning disable
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static T FindFirstObjectByType<T>() where T : Object => null;
        public static implicit operator bool(Object o) => o != null;
    }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
        public T GetComponent<T>() => default;
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public string tag { get; set; }
        public Transform transform => null;
        public T AddComponent<T>() where T : Component => default;
        public T GetComponent<T>() => default;
        public void SetActive(bool active) { }
        public static GameObject CreatePrimitive(PrimitiveType type) => new GameObject();
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 forward => new Vector3(0, 0, 1);
        public Vector3 right => new Vector3(1, 0, 0);
        public Vector3 up => new Vector3(0, 1, 0);
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public Transform Find(string n) => null;
        public void Rotate(Vector3 eulers) { }
        public void LookAt(Vector3 worldPosition) { }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 zero => new Vector3();
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public Vector3 normalized => this;
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => 0f;
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static float Distance(Vector3 a, Vector3 b) => 0f;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a;
    }

    public struct Quaternion
    {
        public static Quaternion identity => new Quaternion();
        public static Quaternion Euler(float x, float y, float z) => new Quaternion();
        public static Quaternion operator *(Quaternion a, Quaternion b) => a;
        public static Vector3 operator *(Quaternion rotation, Vector3 point) => point;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
        public static Color black => new Color(0, 0, 0);
        public static Color Lerp(Color a, Color b, float t) => a;
        public static Color HSVToRGB(float h, float s, float v) => new Color(h, s, v);
    }

    public static class Mathf
    {
        public const float PI = 3.14159274f;
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Abs(float v) => Math.Abs(v);
        public static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
        public static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Pow(float b, float e) => (float)Math.Pow(b, e);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float InverseLerp(float a, float b, float value) => 0f;
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-6f;
        public const float Epsilon = 1.4e-45f;
        public static float Repeat(float t, float length) => t % length;
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { }
    }

    public struct Ray
    {
        public Vector3 origin;
        public Vector3 direction;
        public Ray(Vector3 origin, Vector3 direction) { this.origin = origin; this.direction = direction; }
    }

    public enum KeyCode
    {
        None, Space, Escape, Delete, Return, Tab,
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Alpha0, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        LeftBracket, RightBracket, LeftShift, RightShift, LeftControl,
        Mouse0, Mouse1, Mouse2, UpArrow, DownArrow, LeftArrow, RightArrow
    }

    public static class Input
    {
        public static Vector3 mousePosition => new Vector3();
        public static float GetAxis(string axis) => 0f;
        public static bool GetKey(KeyCode key) => false;
        public static bool GetKeyDown(KeyCode key) => false;
        public static bool GetMouseButton(int button) => false;
        public static bool GetMouseButtonDown(int button) => false;
        public static float mouseScrollDelta => 0f;
    }

    public class Camera : Behaviour
    {
        public static Camera main => null;
        public float fieldOfView { get; set; }
        public float farClipPlane { get; set; }
        public float nearClipPlane { get; set; }
        public Ray ScreenPointToRay(Vector3 screenPoint) => new Ray();
    }

    public enum LightType { Directional, Point, Spot }

    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
    }

    public static class RenderSettings
    {
        public static float ambientIntensity { get; set; }
    }

    public class Shader : Object
    {
        public static Shader Find(string name) => null;
    }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public Material(Material source) { }
        public Color color { get; set; }
        public Texture mainTexture { get; set; }
    }

    public class Renderer : Component
    {
        public Material sharedMaterial { get; set; }
        public Material material { get; set; }
    }

    public class MeshRenderer : Renderer
    {
    }

    public class Mesh : Object
    {
        public int subMeshCount { get; set; }
        public Rendering.IndexFormat indexFormat { get; set; }
        public void SetVertices(List<Vector3> vertices) { }
        public void SetTriangles(List<int> triangles, int submesh) { }
        public void SetUVs(int channel, List<Vector2> uvs) { }
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
        public void Clear() { }
    }

    public class MeshFilter : Component
    {
        public Mesh mesh { get; set; }
        public Mesh sharedMesh { get; set; }
    }

    public class Collider : Component
    {
    }

    public class BoxCollider : Collider
    {
    }

    public enum TextureFormat { RGBA32 }

    public class Texture : Object
    {
        public int width => 0;
        public int height => 0;
    }

    public enum TextureWrapMode { Repeat, Clamp }
    public enum FilterMode { Point, Bilinear, Trilinear }

    public class Texture2D : Texture
    {
        public Texture2D(int width, int height) { }
        public Texture2D(int width, int height, TextureFormat format, bool mipChain) { }
        public TextureWrapMode wrapMode { get; set; }
        public FilterMode filterMode { get; set; }
        public void SetPixel(int x, int y, Color color) { }
        public Color GetPixel(int x, int y) => new Color();
        public void SetPixels(Color[] colors) { }
        public void Apply() { }
        public void Apply(bool updateMipmaps) { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
        public byte[] EncodeToPNG() => Array.Empty<byte>();
        public bool LoadImage(byte[] data) => true;
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => default;
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => null;
    }

    public static class Application
    {
        public static string dataPath => "";
        public static string persistentDataPath => "";
        public static bool runInBackground { get; set; }
        public static void Quit() { }
    }

    public static class Time
    {
        public static float unscaledDeltaTime => 0f;
        public static float deltaTime => 0f;
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }

    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    public enum ColorSpace { Gamma, Linear }

    public class GUIStyle
    {
    }

    public class GUISkin : ScriptableObject
    {
        public GUIStyle box => null;
        public GUIStyle label => null;
    }

    public static class GUI
    {
        public static GUISkin skin { get; set; }
    }

    public static class GUILayout
    {
        public static void BeginArea(Rect screenRect) { }
        public static void BeginArea(Rect screenRect, GUIStyle style) { }
        public static void EndArea() { }
        public static void Label(string text) { }
        public static void Label(string text, params object[] options) { }
    }

    public static class Screen
    {
        public static int width => 1920;
        public static int height => 1080;
    }

    public static class ScreenCapture
    {
        public static void CaptureScreenshot(string filename) { }
    }

    public enum ShadowQuality { Disable, HardOnly, All }

    public static class QualitySettings
    {
        public static int vSyncCount { get; set; }
        public static ShadowQuality shadows { get; set; }
        public static Rendering.RenderPipelineAsset renderPipeline { get; set; }
    }

    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }

    public class RenderPipelineAsset : ScriptableObject
    {
    }

    public static class GraphicsSettings
    {
        public static RenderPipelineAsset defaultRenderPipeline { get; set; }
        public static RenderPipelineAsset currentRenderPipeline => defaultRenderPipeline;
    }
}

namespace UnityEngine.Rendering.Universal
{
    public class ScriptableRendererData : ScriptableObject
    {
    }

    public class UniversalRendererData : ScriptableRendererData
    {
    }

    public class UniversalRenderPipelineAsset : Rendering.RenderPipelineAsset
    {
        public static UniversalRenderPipelineAsset Create(ScriptableRendererData rendererData = null) => null;
        public bool supportsHDR { get; set; }
        public int shadowCascadeCount { get; set; }
        public float shadowDistance { get; set; }
        public float renderScale { get; set; }
    }
}
