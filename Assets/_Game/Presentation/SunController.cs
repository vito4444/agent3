using UnityEngine;
using Starsoil.Core;

namespace Starsoil.Presentation
{
    /// <summary>Day/night lighting driven by sim time (docs/plan/02: 12h day + 12h night).</summary>
    public sealed class SunController : MonoBehaviour
    {
        private const float NightIntensity = 0.08f;
        private const float DayIntensity = 1.15f;
        private const float SunYaw = -30f;

        private Light _sun;
        private World _world;

        public void Init(World world, Light sun)
        {
            _world = world;
            _sun = sun;
        }

        public void SwitchWorld(World world)
        {
            _world = world;
        }

        private void LateUpdate()
        {
            if (_world == null || _sun == null)
            {
                return;
            }
            float dayFraction = (_world.Tick % GameConstants.TicksPerDay) / (float)GameConstants.TicksPerDay;
            // Hour 6 = sunrise on the horizon; hour 18 = sunset.
            float sunAngle = (dayFraction - 0.25f) * 360f;
            _sun.transform.rotation = Quaternion.Euler(sunAngle, SunYaw, 0f);
            bool day = !_world.IsNight;
            _sun.intensity = day ? DayIntensity : NightIntensity;
            RenderSettings.ambientIntensity = day ? 1f : 0.35f;
        }
    }
}
