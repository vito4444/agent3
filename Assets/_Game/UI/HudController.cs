using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Starsoil.Core;
using Starsoil.Data;

namespace Starsoil.UI
{
    /// <summary>
    /// M1 HUD (UI Toolkit, tree built in code so no hand-authored UXML assets are needed):
    /// top resource bar, alert list with jump-to-location, tutorial box with skip,
    /// critical banner and defeat overlay. All strings come from L10n (docs/plan/10).
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        private World _world;
        private System.Func<int> _speed;
        private System.Action<int, int> _jumpTo;
        private System.Action _newGame;
        private System.Action _skipTutorial;

        private Label _topBar;
        private Label _banner;
        private VisualElement _alertsBox;
        /// <summary>Below this window width the key-hint segment overflows; hide it.</summary>
        private const int WideTopBarMinWidth = 1150;

        private VisualElement _tutorialBox;
        private Label _tutorialLabel;
        private VisualElement _defeatOverlay;
        private Label _defeatText;
        private readonly Dictionary<string, Button> _alertButtons = new Dictionary<string, Button>();
        private bool _uiReady;

        public void Init(World world, System.Func<int> speedGetter,
            System.Action<int, int> jumpTo, System.Action newGame, System.Action skipTutorial)
        {
            _world = world;
            _speed = speedGetter;
            _jumpTo = jumpTo;
            _newGame = newGame;
            _skipTutorial = skipTutorial;
            BuildUi();
        }

        public void SwitchWorld(World world)
        {
            _world = world;
            if (_defeatOverlay != null)
            {
                _defeatOverlay.style.display = DisplayStyle.None;
            }
        }

        private void BuildUi()
        {
            var panelSettings = Resources.Load<PanelSettings>("StarsoilPanelSettings");
            if (panelSettings == null)
            {
                Debug.LogWarning("[HUD] StarsoilPanelSettings missing (run Starsoil/Setup Project); falling back to debug overlay only.");
                return;
            }
            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            var root = doc.rootVisualElement;
            root.style.flexGrow = 1f;

            _topBar = new Label();
            Style(_topBar, new Color(0.08f, 0.09f, 0.12f, 0.92f), Color.white);
            _topBar.style.position = Position.Absolute;
            _topBar.style.top = 0;
            _topBar.style.left = 0;
            _topBar.style.right = 0;
            _topBar.style.unityTextAlign = TextAnchor.MiddleLeft;
            root.Add(_topBar);

            _banner = new Label();
            Style(_banner, new Color(0.75f, 0.15f, 0.1f, 0.95f), Color.white);
            _banner.style.position = Position.Absolute;
            _banner.style.top = 30;
            _banner.style.left = 0;
            _banner.style.right = 0;
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _banner.style.display = DisplayStyle.None;
            root.Add(_banner);

            _alertsBox = new VisualElement();
            _alertsBox.style.position = Position.Absolute;
            _alertsBox.style.bottom = 8;
            _alertsBox.style.left = 8;
            root.Add(_alertsBox);

            // Slim full-width strip right under the top bar: every panel opens at
            // top>=60, so the tutorial never overlaps them (visual QA round 1).
            _tutorialBox = new VisualElement();
            _tutorialBox.style.position = Position.Absolute;
            _tutorialBox.style.top = 30;
            _tutorialBox.style.left = Length.Percent(22f);
            _tutorialBox.style.right = Length.Percent(22f);
            _tutorialBox.style.flexDirection = FlexDirection.Row;
            _tutorialBox.style.alignItems = Align.Center;
            _tutorialBox.style.justifyContent = Justify.SpaceBetween;
            Style(_tutorialBox, new Color(0.1f, 0.14f, 0.1f, 0.9f), Color.white);
            _tutorialBox.style.paddingTop = 2;
            _tutorialBox.style.paddingBottom = 2;
            _tutorialBox.style.maxHeight = 28;
            _tutorialLabel = new Label { text = string.Empty };
            _tutorialLabel.style.whiteSpace = WhiteSpace.Normal;
            _tutorialLabel.style.color = Color.white;
            _tutorialLabel.style.flexGrow = 1f;
            _tutorialBox.Add(_tutorialLabel);
            var skip = new Button(() => _skipTutorial?.Invoke()) { text = L10n.Tr("ui_skip_tutorial") };
            _tutorialBox.Add(skip);
            root.Add(_tutorialBox);

            _defeatOverlay = new VisualElement();
            _defeatOverlay.style.position = Position.Absolute;
            _defeatOverlay.style.top = 0;
            _defeatOverlay.style.bottom = 0;
            _defeatOverlay.style.left = 0;
            _defeatOverlay.style.right = 0;
            _defeatOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.82f);
            _defeatOverlay.style.alignItems = Align.Center;
            _defeatOverlay.style.justifyContent = Justify.Center;
            _defeatOverlay.style.display = DisplayStyle.None;
            var title = new Label();
            title.text = L10n.Tr("defeat_title");
            title.style.fontSize = 34;
            title.style.color = new Color(0.95f, 0.35f, 0.3f);
            _defeatOverlay.Add(title);
            _defeatText = new Label { text = string.Empty };
            _defeatText.style.color = Color.white;
            _defeatText.style.whiteSpace = WhiteSpace.Normal;
            _defeatOverlay.Add(_defeatText);
            var newGameButton = new Button(() => _newGame?.Invoke()) { text = L10n.Tr("defeat_new_game") };
            _defeatOverlay.Add(newGameButton);
            var loadHint = new Label { text = L10n.Tr("defeat_load") + " (F9)" };
            loadHint.style.color = new Color(0.8f, 0.8f, 0.8f);
            _defeatOverlay.Add(loadHint);
            root.Add(_defeatOverlay);

            _uiReady = true;
        }

        private static void Style(VisualElement element, Color background, Color text)
        {
            element.style.backgroundColor = background;
            element.style.color = text;
            element.style.paddingLeft = 10;
            element.style.paddingRight = 10;
            element.style.paddingTop = 6;
            element.style.paddingBottom = 6;
        }

        private void Update()
        {
            if (!_uiReady || _world == null)
            {
                return;
            }
            RefreshTopBar();
            RefreshBanner();
            RefreshAlerts();
            RefreshTutorial();
            RefreshDefeat();
        }

        private void RefreshTopBar()
        {
            int food = 0;
            foreach (string f in ItemIds.Foods)
            {
                food += _world.CountItemEverywhere(f);
            }
            float networkO2 = _world.Life.TankO2;
            foreach (var pair in _world.Networks.GasStored)
            {
                networkO2 += pair.Value;
            }
            string speed = _speed() == 0 ? L10n.Tr("hud_speed_paused") : "x" + _speed();
            // Power overview per M2-T1: supply / demand / battery store (crank ledger included).
            string power = (_world.Networks.LastSupplyKw + _world.Life.PowerKw).ToString("F0") + "/" +
                           _world.Networks.LastDemandKw.ToString("F0") + "kW ⚡" +
                           _world.Networks.BatteryStoredKwh.ToString("F0") + "kWh";
            _topBar.text =
                L10n.Tr("hud_power") + " " + power + " · " +
                L10n.Tr("hud_oxygen") + " " + networkO2.ToString("F0") + " · " +
                L10n.Tr("hud_water") + " " + _world.CountItemEverywhere(ItemIds.Water) + " · " +
                L10n.Tr("hud_food") + " " + food + " · " +
                L10n.Tr("hud_credits") + " 0 · " +
                L10n.Tr("hud_population") + " " + _world.Colonists.AliveCount + " (" + _world.Bots.All.Count + "🤖) · " +
                L10n.TrF("hud_day", _world.Day) + " " + L10n.TrF("hud_hour", _world.HourOfDay) + " · " + speed +
                (Screen.width >= WideTopBarMinWidth ? "  |  " + L10n.Tr("ui_speed_hint") : string.Empty);
        }

        private void RefreshBanner()
        {
            bool o2Low = _world.Alerts.Active.ContainsKey(AlertIds.OxygenLow);
            _banner.style.display = o2Low ? DisplayStyle.Flex : DisplayStyle.None;
            if (o2Low)
            {
                _banner.text = L10n.Tr("alert_o2_low");
            }
        }

        private void RefreshAlerts()
        {
            foreach (var pair in _world.Alerts.Active)
            {
                if (_alertButtons.ContainsKey(pair.Key))
                {
                    continue;
                }
                var alert = pair.Value;
                string key = alert.Id.StartsWith(AlertIds.ColonistCritical) ? "alert_colonist_critical" : "alert_" + alert.Id;
                var button = new Button(() => _jumpTo?.Invoke(alert.X, alert.Y)) { text = L10n.Tr(key) };
                button.style.backgroundColor = alert.Severity == AlertSeverity.Critical
                    ? new Color(0.7f, 0.15f, 0.1f, 0.95f)
                    : new Color(0.7f, 0.55f, 0.1f, 0.95f);
                button.style.color = Color.white;
                _alertsBox.Add(button);
                _alertButtons.Add(pair.Key, button);
            }
            var stale = new List<string>();
            foreach (var pair in _alertButtons)
            {
                if (!_world.Alerts.Active.ContainsKey(pair.Key))
                {
                    stale.Add(pair.Key);
                }
            }
            foreach (string id in stale)
            {
                _alertsBox.Remove(_alertButtons[id]);
                _alertButtons.Remove(id);
            }
        }

        private void RefreshTutorial()
        {
            if (_world.Tutorial.Completed)
            {
                _tutorialBox.style.display = DisplayStyle.None;
                return;
            }
            _tutorialBox.style.display = DisplayStyle.Flex;
            _tutorialLabel.text = L10n.Tr("tutorial_step_" + _world.Tutorial.CurrentStepId);
        }

        private void RefreshDefeat()
        {
            if (!_world.Defeated)
            {
                _defeatOverlay.style.display = DisplayStyle.None;
                return;
            }
            if (_defeatOverlay.style.display != DisplayStyle.Flex)
            {
                var lines = new List<string>
                {
                    L10n.TrF("defeat_days", _world.Day),
                    L10n.Tr("defeat_deaths")
                };
                foreach (var pair in _world.Stats.Deaths)
                {
                    lines.Add(L10n.Tr("death_" + pair.Key.ToLowerInvariant()) + " x" + pair.Value);
                }
                _defeatText.text = string.Join("\n", lines);
                _defeatOverlay.style.display = DisplayStyle.Flex;
            }
        }
    }
}
