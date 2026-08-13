using System;
using NUnit.Framework;
using UnityEngine;
using Starsoil.Core;
using Starsoil.Data;
using Starsoil.UI;
using Starsoil.Bootstrap;

namespace Starsoil.Tests
{
    /// <summary>
    /// EditMode smoke: every UI controller and the sim driver must survive Init against
    /// a live universe without throwing. When SetupProject has created the PanelSettings
    /// resource the full UI build path runs; without it the controllers take their
    /// guarded early-out — both are valid smoke outcomes, exceptions are not.
    /// </summary>
    public sealed class UiSmokeTests
    {
        private static Universe NewUniverse()
        {
            var universe = Universe.NewGame(12UL, 96);
            CatalogContent.ApplyTo(universe);
            return universe;
        }

        private static void SmokeComponent<T>(Action<T> init) where T : Component
        {
            var host = new GameObject("smoke_" + typeof(T).Name);
            try
            {
                var component = host.AddComponent<T>();
                Assert.DoesNotThrow(() => init(component), typeof(T).Name + ".Init 不得抛异常");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void UiControllers_Init_DoesNotThrow()
        {
            var universe = NewUniverse();
            var world = universe.ActiveWorld;

            SmokeComponent<CraftPanelController>(c => c.Init(world));
            SmokeComponent<TechPanelController>(c => c.Init(world));
            SmokeComponent<JobsPanelController>(c => c.Init(world));
            SmokeComponent<RecipeBrowserController>(c => c.Init(world));
            SmokeComponent<StarMapController>(c => c.Init(universe, _ => { }));
            SmokeComponent<TradePanelController>(c => c.Init(universe));
        }

        [Test]
        public void SimDriver_AttachUniverse_AndStep()
        {
            var universe = NewUniverse();
            var host = new GameObject("smoke_SimDriver");
            try
            {
                var driver = host.AddComponent<SimDriver>();
                Assert.DoesNotThrow(() => driver.AttachUniverse(universe));
                long before = universe.Tick;
                for (int i = 0; i < 30; i++)
                {
                    universe.Step();
                }
                Assert.AreEqual(before + 30, universe.Tick, "宇宙推进确定性 tick");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
