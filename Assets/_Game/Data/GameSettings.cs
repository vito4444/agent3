using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Starsoil.Data
{
    /// <summary>
    /// Player settings persisted as JSON (docs/plan/10 accessibility groundwork:
    /// auto-pause on critical alerts is on by default and can be disabled).
    /// </summary>
    public sealed class GameSettings
    {
        public bool AutoPauseOnCritical = true;
        public bool SkipTutorial;
        public string Language = "zh";

        private static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        public static GameSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    return JsonConvert.DeserializeObject<GameSettings>(File.ReadAllText(FilePath)) ?? new GameSettings();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Settings] load failed, using defaults: " + e.Message);
            }
            return new GameSettings();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Settings] save failed: " + e.Message);
            }
        }
    }
}
