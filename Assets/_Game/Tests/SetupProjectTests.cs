using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Starsoil.EditorTools;

namespace Starsoil.Tests
{
    /// <summary>
    /// EditMode check for the first-open automation (docs/plan/08 M0-T2): SetupProject
    /// must be idempotent and leave the runtime-required PanelSettings resource loadable.
    /// </summary>
    public sealed class SetupProjectTests
    {
        private const string PanelSettingsPath = "Assets/Resources/StarsoilPanelSettings.asset";

        [Test]
        public void Run_Twice_IsIdempotent_AndCreatesPanelSettings()
        {
            Assert.DoesNotThrow(SetupProject.Run, "首次运行不得抛异常");
            Assert.DoesNotThrow(SetupProject.Run, "重复运行必须幂等");

            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.IsNotNull(panelSettings, "PanelSettings 资产存在于 " + PanelSettingsPath);
        }
    }
}
