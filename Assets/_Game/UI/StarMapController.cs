using UnityEngine;
using UnityEngine.UIElements;
using Starsoil.Core;
using Starsoil.Data;

namespace Starsoil.UI
{
    /// <summary>
    /// L4 star map panel (M4, docs/plan/05): M key toggles; lists the 曦光 bodies with
    /// travel time and resources, live transits, and every colony region with a switch
    /// button. Pad orders are configured here (target body for the next colonist pod).
    /// </summary>
    public sealed class StarMapController : MonoBehaviour
    {
        private Universe _universe;
        private System.Action<int> _switchRegion;
        private VisualElement _panel;
        private ScrollView _list;
        private bool _uiReady;
        private long _lastRefreshTick = -1;

        public void Init(Universe universe, System.Action<int> switchRegion)
        {
            _universe = universe;
            _switchRegion = switchRegion;
            BuildUi();
        }

        private void BuildUi()
        {
            var panelSettings = Resources.Load<PanelSettings>("StarsoilPanelSettings");
            if (panelSettings == null)
            {
                return;
            }
            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.sortingOrder = 40;
            var root = doc.rootVisualElement;

            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.left = Length.Percent(25f);
            _panel.style.right = Length.Percent(25f);
            _panel.style.top = 60;
            _panel.style.maxHeight = 620;
            _panel.style.backgroundColor = new Color(0.05f, 0.06f, 0.12f, 0.97f);
            _panel.style.paddingLeft = 12;
            _panel.style.paddingRight = 12;
            _panel.style.paddingTop = 8;
            _panel.style.paddingBottom = 8;
            _panel.style.display = DisplayStyle.None;

            var title = new Label { text = L10n.Tr("ui_star_map") };
            title.style.color = Color.white;
            title.style.fontSize = 16;
            _panel.Add(title);

            _list = new ScrollView();
            _list.style.maxHeight = 560;
            _panel.Add(_list);
            root.Add(_panel);
            _uiReady = true;
        }

        private void Update()
        {
            if (!_uiReady || _universe == null)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.M))
            {
                bool visible = _panel.style.display == DisplayStyle.Flex;
                _panel.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
                _lastRefreshTick = -1;
            }
            if (_panel.style.display == DisplayStyle.Flex && _universe.Tick != _lastRefreshTick &&
                _universe.Tick % GameConstants.TicksPerHour == 0)
            {
                Rebuild();
            }
            if (_panel.style.display == DisplayStyle.Flex && _lastRefreshTick < 0)
            {
                Rebuild();
            }
        }

        private void Rebuild()
        {
            _lastRefreshTick = _universe.Tick;
            _list.Clear();

            var regionsHeader = new Label { text = L10n.Tr("ui_regions") };
            regionsHeader.style.color = new Color(0.7f, 0.85f, 0.95f);
            _list.Add(regionsHeader);
            AddRegionRow(_universe.ActiveRegionId, _universe.ActiveBodyId,
                _universe.ActiveWorld.Colonists.AliveCount, active: true);
            foreach (var pair in _universe.FrozenRegions)
            {
                AddRegionRow(pair.Key, pair.Value.BodyId, pair.Value.ColonistCount, active: false);
            }

            if (_universe.Transits.Count > 0)
            {
                var transitHeader = new Label { text = L10n.Tr("ui_transits") };
                transitHeader.style.color = new Color(0.7f, 0.85f, 0.95f);
                transitHeader.style.marginTop = 6;
                _list.Add(transitHeader);
                foreach (var transit in _universe.Transits)
                {
                    long etaHours = (transit.ArriveTick - _universe.Tick) / GameConstants.TicksPerHour;
                    var row = new Label
                    {
                        text = transit.Payload + " → " + BodyName(transit.TargetBodyId) + "  ETA " + etaHours + "h"
                    };
                    row.style.color = new Color(0.85f, 0.8f, 0.6f);
                    _list.Add(row);
                }
            }

            var bodiesHeader = new Label { text = L10n.Tr("ui_bodies") };
            bodiesHeader.style.color = new Color(0.7f, 0.85f, 0.95f);
            bodiesHeader.style.marginTop = 6;
            _list.Add(bodiesHeader);
            foreach (var pair in _universe.Bodies.All)
            {
                var body = pair.Value;
                long travelHours = _universe.TransferTicks(_universe.ActiveBodyId, body.Id) / GameConstants.TicksPerHour;
                var row = new Label
                {
                    text = (L10n.Language == "en" ? body.En : body.Zh) +
                           (body.Landable ? "" : " ✕") + "  " +
                           L10n.TrF("ui_travel_hours", travelHours) + "  [" +
                           string.Join("/", body.Resources.ConvertAll(ItemCatalog.NameOf)) + "]"
                };
                row.style.color = body.Landable ? Color.white : new Color(0.55f, 0.55f, 0.6f);
                row.style.whiteSpace = WhiteSpace.Normal;
                row.style.marginBottom = 2;
                _list.Add(row);
            }
        }

        private void AddRegionRow(int regionId, string bodyId, int colonists, bool active)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var label = new Label
            {
                text = (active ? "▶ " : "  ") + L10n.TrF("ui_region_row", regionId, BodyName(bodyId), colonists)
            };
            label.style.color = active ? new Color(0.6f, 0.95f, 0.6f) : Color.white;
            label.style.flexGrow = 1f;
            row.Add(label);
            if (!active)
            {
                int captured = regionId;
                row.Add(new Button(() => _switchRegion?.Invoke(captured)) { text = L10n.Tr("ui_switch_region") });
            }
            _list.Add(row);
        }

        private string BodyName(string bodyId)
        {
            return _universe.Bodies.TryGet(bodyId, out var body)
                ? (L10n.Language == "en" ? body.En : body.Zh)
                : bodyId;
        }
    }
}
