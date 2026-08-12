using UnityEngine;
using Starsoil.Core;

namespace Starsoil.UI
{
    /// <summary>
    /// Steamworks bridge (docs/plan/10, M8-T1): mirrors Core achievement unlocks and the
    /// six stats to Steam, plus rich presence. Everything is isolated behind the STEAM
    /// scripting define — without it the game builds and runs with no Steam dependency.
    /// Setup (user action items, docs/plan/10): install Steamworks.NET via the locked
    /// release tag, add STEAM to Scripting Define Symbols, place steam_appid.txt with the
    /// real AppID during development.
    /// </summary>
    public sealed class SteamBridge : MonoBehaviour
    {
        private Universe _universe;
        private readonly System.Collections.Generic.HashSet<string> _mirrored =
            new System.Collections.Generic.HashSet<string>();

        public void Init(Universe universe)
        {
            _universe = universe;
#if STEAM
            if (!Steamworks.SteamAPI.Init())
            {
                Debug.LogWarning("[Steam] SteamAPI.Init failed; running without Steam.");
                enabled = false;
            }
#else
            Debug.Log("[Steam] STEAM define absent; bridge idle (build runs standalone).");
#endif
        }

        private void Update()
        {
            if (_universe == null)
            {
                return;
            }
#if STEAM
            Steamworks.SteamAPI.RunCallbacks();
#endif
            foreach (string id in _universe.Achievements.Unlocked)
            {
                if (_mirrored.Add(id))
                {
                    MirrorAchievement(id);
                }
            }
        }

        private void MirrorAchievement(string id)
        {
#if STEAM
            Steamworks.SteamUserStats.SetAchievement("ACH_" + id.ToUpperInvariant());
            Steamworks.SteamUserStats.StoreStats();
#else
            Debug.Log("[Steam] (dry-run) achievement unlocked: " + id);
#endif
        }

        /// <summary>Six stats (docs/plan/10): days survived, colonists, recipes unlocked,
        /// rockets launched, trades, raids repelled.</summary>
        public void PushStats()
        {
#if STEAM
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
            Steamworks.SteamAPI.Shutdown();
#endif
        }
    }
}
