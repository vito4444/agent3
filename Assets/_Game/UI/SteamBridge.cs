using UnityEngine;
using Starsoil.Core;

namespace Starsoil.UI
{
    /// <summary>
    /// Steamworks bridge (docs/plan/10, M8-T1): mirrors Core achievement unlocks and
    /// stats to Steam, publishes rich presence, and exposes the Steam UI language.
    /// Everything is isolated behind the STEAM scripting define — without it the game
    /// builds and runs with no Steam dependency; with it but without a Steam client the
    /// bridge disables itself gracefully (Init failure or missing steam_api library).
    /// Setup (user action items, docs/plan/10): install Steamworks.NET (locked release
    /// tag in Packages/manifest.json), add STEAM to Scripting Define Symbols, place
    /// steam_appid.txt with the real AppID during development.
    /// </summary>
    public sealed class SteamBridge : MonoBehaviour
    {
        private Universe _universe;
        private bool _apiReady;
        private long _lastPresenceDay = -1;
        private readonly System.Collections.Generic.HashSet<string> _mirrored =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>Boots the Steam API before any UI exists so the UI language can
        /// follow Steam. Safe to call without a Steam client.</summary>
        public void InitApi()
        {
#if STEAM
            try
            {
                _apiReady = Steamworks.SteamAPI.Init();
            }
            catch (System.Exception error)
            {
                // Missing steam_api library or client: run standalone.
                Debug.LogWarning("[Steam] init failed (" + error.GetType().Name + "); running without Steam.");
                _apiReady = false;
            }
            if (!_apiReady)
            {
                Debug.LogWarning("[Steam] SteamAPI unavailable; bridge idle.");
            }
#else
            Debug.Log("[Steam] STEAM define absent; bridge idle (build runs standalone).");
#endif
        }

        public void AttachUniverse(Universe universe)
        {
            _universe = universe;
        }

        /// <summary>Steam UI language mapped to the game's language codes; false when
        /// Steam is unavailable (caller falls back to settings/default).</summary>
        public bool TryGetLanguage(out string language)
        {
            language = null;
#if STEAM
            if (_apiReady)
            {
                string steamLanguage = Steamworks.SteamApps.GetCurrentGameLanguage();
                language = steamLanguage == "schinese" || steamLanguage == "tchinese" ? "zh" : "en";
                return true;
            }
#endif
            return false;
        }

        private void Update()
        {
            if (_universe == null)
            {
                return;
            }
#if STEAM
            if (_apiReady)
            {
                Steamworks.SteamAPI.RunCallbacks();
            }
#endif
            foreach (string id in _universe.Achievements.Unlocked)
            {
                if (_mirrored.Add(id))
                {
                    MirrorAchievement(id);
                }
            }
            long day = _universe.ActiveWorld.Day;
            if (day != _lastPresenceDay)
            {
                _lastPresenceDay = day;
                PushPresence(day);
            }
        }

        private void MirrorAchievement(string id)
        {
#if STEAM
            if (_apiReady)
            {
                Steamworks.SteamUserStats.SetAchievement("ACH_" + id.ToUpperInvariant());
                Steamworks.SteamUserStats.StoreStats();
                return;
            }
#endif
            Debug.Log("[Steam] (dry-run) achievement unlocked: " + id);
        }

        /// <summary>Rich presence: day + population + region count, refreshed daily.</summary>
        private void PushPresence(long day)
        {
#if STEAM
            if (_apiReady)
            {
                string status = "Day " + day + " · " + _universe.ActiveWorld.Colonists.AliveCount +
                                " colonists · " + (1 + _universe.FrozenRegions.Count) + " regions";
                Steamworks.SteamFriends.SetRichPresence("status", status);
            }
#endif
        }

        /// <summary>Six stats (docs/plan/10): days survived, colonists, recipes unlocked,
        /// rockets launched, trades, raids repelled.</summary>
        public void PushStats()
        {
#if STEAM
            if (!_apiReady)
            {
                return;
            }
            Steamworks.SteamUserStats.SetStat("stat_days", (int)_universe.ActiveWorld.Day);
            Steamworks.SteamUserStats.SetStat("stat_colonists", _universe.ActiveWorld.Colonists.AliveCount);
            Steamworks.SteamUserStats.SetStat("stat_trades", _universe.Achievements.TradesCompleted);
            Steamworks.SteamUserStats.SetStat("stat_raids_repelled", _universe.Achievements.RaidsRepelled);
            Steamworks.SteamUserStats.StoreStats();
#endif
        }

        private void OnDestroy()
        {
#if STEAM
            if (_apiReady)
            {
                Steamworks.SteamAPI.Shutdown();
            }
#endif
        }
    }
}
