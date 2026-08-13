// Steamworks.NET surface stubs (see UnityEngineStubs.cs header): the compile check
// builds with the STEAM define so the SteamBridge #if STEAM branches are type-checked
// without the real package or a Steam client.
// ReSharper disable all
#pragma warning disable
namespace Steamworks
{
    public static class SteamAPI
    {
        public static bool Init() => false;
        public static void RunCallbacks() { }
        public static void Shutdown() { }
    }

    public static class SteamUserStats
    {
        public static bool SetAchievement(string name) => true;
        public static bool StoreStats() => true;
        public static bool SetStat(string name, int value) => true;
    }

    public static class SteamFriends
    {
        public static bool SetRichPresence(string key, string value) => true;
    }

    public static class SteamApps
    {
        public static string GetCurrentGameLanguage() => "english";
    }
}
