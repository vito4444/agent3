using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Starsoil.Core;
using Starsoil.Data;

namespace Starsoil.UI
{
    /// <summary>
    /// Tech tree panel v1 (M2-T11): T key toggles; nodes grouped by domain with state
    /// (unlocked / researchable / locked), core costs, and a research button. The full
    /// tree visualization arrives with M3's 90-node content.
    /// </summary>
    public sealed class TechPanelController : MonoBehaviour
    {
        private World _world;
        private VisualElement _panel;
        private ScrollView _list;
        private bool _uiReady;
        private long _lastRefreshTick = -1;

        public void Init(World world)
        {
            _world = world;
            BuildUi();
        }

        public void SwitchWorld(World world)
        {
            _world = world;
            _lastRefreshTick = -1;
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
            doc.sortingOrder = 20;
            var root = doc.rootVisualElement;

            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.right = 8;
            _panel.style.top = 60;
            _panel.style.width = 420;
            _panel.style.maxHeight = 640;
            _panel.style.backgroundColor = new Color(0.08f, 0.1f, 0.16f, 0.96f);
            _panel.style.paddingLeft = 10;
            _panel.style.paddingRight = 10;
            _panel.style.paddingTop = 8;
            _panel.style.paddingBottom = 8;
            _panel.style.display = DisplayStyle.None;

            var title = new Label { text = L10n.Tr("ui_tech_tree") };
            title.style.color = Color.white;
            title.style.fontSize = 16;
            _panel.Add(title);

            _list = new ScrollView();
            _list.style.maxHeight = 580;
            _panel.Add(_list);

            root.Add(_panel);
            _uiReady = true;
        }

        private void Update()
        {
            if (!_uiReady || _world == null)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.T))
            {
                bool visible = _panel.style.display == DisplayStyle.Flex;
                _panel.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
                _lastRefreshTick = -1;
            }
            if (_panel.style.display == DisplayStyle.Flex && _world.Tick != _lastRefreshTick &&
                _world.Tick % Balance.DispatchIntervalTicks == 0)
            {
                _lastRefreshTick = _world.Tick;
                Rebuild();
            }
        }

        private void Rebuild()
        {
            _list.Clear();
            var byDomain = new SortedDictionary<string, List<TechNode>>();
            foreach (var pair in _world.Tech.Nodes)
            {
                if (!byDomain.TryGetValue(pair.Value.Domain, out var list))
                {
                    list = new List<TechNode>();
                    byDomain.Add(pair.Value.Domain, list);
                }
                list.Add(pair.Value);
            }
            foreach (var domain in byDomain)
            {
                var header = new Label { text = domain.Key };
                header.style.color = new Color(0.7f, 0.8f, 0.95f);
                header.style.marginTop = 6;
                _list.Add(header);
                domain.Value.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                foreach (var node in domain.Value)
                {
                    _list.Add(BuildRow(node));
                }
            }
        }

        private VisualElement BuildRow(TechNode node)
        {
            var row = new VisualElement();
            row.style.backgroundColor = new Color(0.12f, 0.14f, 0.2f, 0.92f);
            row.style.marginBottom = 3;
            row.style.paddingLeft = 6;
            row.style.paddingRight = 6;
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;

            bool unlocked = _world.Tech.IsUnlocked(node.Id);
            bool researching = _world.Tech.ResearchTarget == node.Id;
            bool selectable = _world.Tech.CanSelectTarget(node.Id);

            string name = L10n.Language == "en" ? node.En : node.Zh;
            var label = new Label
            {
                text = (unlocked ? "✓ " : researching ? "… " : selectable ? "○ " : "🔒 ") + name + CostText(node)
            };
            label.style.color = unlocked ? new Color(0.55f, 0.9f, 0.55f)
                : selectable || researching ? Color.white : new Color(0.55f, 0.55f, 0.6f);
            label.style.whiteSpace = WhiteSpace.Normal;
            row.Add(label);

            if (selectable && !researching)
            {
                var button = new Button(() => _world.Commands.Enqueue(new SetResearchTargetCommand { NodeId = node.Id }))
                {
                    text = L10n.Tr("ui_research")
                };
                row.Add(button);
            }
            return row;
        }

        private string CostText(TechNode node)
        {
            if (node.Cost.Count == 0)
            {
                return string.Empty;
            }
            var parts = new List<string>();
            foreach (var cost in node.Cost)
            {
                int paid = _world.Tech.ResearchTarget == node.Id ? _world.Tech.PaidCores.Get(cost.ItemId) : 0;
                parts.Add(L10n.Tr("item_" + cost.ItemId) + " " + paid + "/" + cost.Count);
            }
            return "  [" + string.Join(", ", parts) + "]";
        }
    }
}
