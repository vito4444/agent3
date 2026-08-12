using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Starsoil.Core;
using Starsoil.Data;

namespace Starsoil.UI
{
    /// <summary>
    /// Station craft orders panel (M1-T5): lists every recipe the clicked station can run
    /// with its rationale line, offers +1 / maintain-stock orders, and shows the queue
    /// with cancel buttons. Opens from PlacementController.StationClicked.
    /// </summary>
    public sealed class CraftPanelController : MonoBehaviour
    {
        private const int DefaultMaintainTarget = 8;

        private World _world;
        private VisualElement _panel;
        private Label _title;
        private VisualElement _recipeList;
        private VisualElement _orderList;
        private int _stationId;
        private bool _uiReady;

        public void Init(World world)
        {
            _world = world;
            BuildUi();
        }

        public void SwitchWorld(World world)
        {
            _world = world;
            Close();
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
            doc.sortingOrder = 10;
            var root = doc.rootVisualElement;

            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.left = 8;
            _panel.style.top = 60;
            _panel.style.width = 460;
            _panel.style.backgroundColor = new Color(0.09f, 0.1f, 0.14f, 0.95f);
            _panel.style.paddingLeft = 10;
            _panel.style.paddingRight = 10;
            _panel.style.paddingTop = 8;
            _panel.style.paddingBottom = 8;
            _panel.style.display = DisplayStyle.None;

            _title = new Label();
            _title.style.color = Color.white;
            _title.style.fontSize = 16;
            _panel.Add(_title);

            var close = new Button(Close) { text = L10n.Tr("ui_cancel") };
            _panel.Add(close);

            var recipesHeader = new Label { text = L10n.Tr("ui_orders") };
            recipesHeader.style.color = new Color(0.8f, 0.85f, 0.9f);
            _panel.Add(recipesHeader);

            _recipeList = new VisualElement();
            _panel.Add(_recipeList);

            _orderList = new VisualElement();
            _panel.Add(_orderList);

            root.Add(_panel);
            _uiReady = true;
        }

        public void Open(int stationId)
        {
            if (!_uiReady || !_world.Buildings.TryGet(stationId, out var station) ||
                !BuildingDefs.TryGet(station.DefId, out var def))
            {
                return;
            }
            _stationId = stationId;
            _title.text = L10n.Tr("building_" + station.DefId);
            _panel.style.display = DisplayStyle.Flex;
            RebuildRecipeRows(def);
        }

        public void Close()
        {
            if (_panel != null)
            {
                _panel.style.display = DisplayStyle.None;
            }
            _stationId = 0;
        }

        private void RebuildRecipeRows(BuildingDef stationDef)
        {
            _recipeList.Clear();
            foreach (var pair in SortedRecipes())
            {
                var recipe = pair.Value;
                if (!recipe.RunsOn(stationDef.Kind, out _) ||
                    !_world.Tech.IsRecipeUnlocked(recipe.Id))
                {
                    continue;
                }
                var row = new VisualElement();
                row.style.marginBottom = 6;
                row.style.backgroundColor = new Color(0.13f, 0.15f, 0.2f, 0.9f);
                row.style.paddingLeft = 6;
                row.style.paddingRight = 6;
                row.style.paddingTop = 4;
                row.style.paddingBottom = 4;

                string outputName = recipe.Outputs.Count > 0 ? L10n.Tr("item_" + recipe.Outputs[0].ItemId) : recipe.Id;
                var name = new Label { text = outputName };
                name.style.color = Color.white;
                row.Add(name);

                var rationale = new Label { text = L10n.Language == "en" ? recipe.RationaleEn : recipe.RationaleZh };
                rationale.style.color = new Color(0.75f, 0.78f, 0.82f);
                rationale.style.whiteSpace = WhiteSpace.Normal;
                rationale.style.fontSize = 11;
                row.Add(rationale);

                var buttons = new VisualElement();
                buttons.style.flexDirection = FlexDirection.Row;
                string recipeId = recipe.Id;
                var plusOne = new Button(() => Enqueue(recipeId, 1, 0)) { text = "+1" };
                buttons.Add(plusOne);
                var maintain = new Button(() => Enqueue(recipeId, -1, DefaultMaintainTarget))
                {
                    text = L10n.Tr("ui_order_maintain") + " " + DefaultMaintainTarget
                };
                buttons.Add(maintain);
                row.Add(buttons);

                _recipeList.Add(row);
            }
        }

        private void Enqueue(string recipeId, int count, int maintainTarget)
        {
            _world.Commands.Enqueue(new AddCraftOrderCommand
            {
                StationId = _stationId,
                RecipeId = recipeId,
                Count = count,
                MaintainTarget = maintainTarget
            });
        }

        private void Update()
        {
            if (!_uiReady || _stationId == 0 || _world == null)
            {
                return;
            }
            RefreshOrders();
        }

        private void RefreshOrders()
        {
            _orderList.Clear();
            if (!_world.Crafting.OrdersByStation.TryGetValue(_stationId, out var orders))
            {
                return;
            }
            foreach (var order in orders)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                string label = order.RecipeId + (order.IsMaintain
                    ? " · " + L10n.Tr("ui_order_maintain") + " " + order.MaintainTarget
                    : " x" + order.Remaining);
                var text = new Label { text = label };
                text.style.color = new Color(0.9f, 0.9f, 0.7f);
                row.Add(text);
                int orderId = order.Id;
                var cancel = new Button(() => _world.Commands.Enqueue(new RemoveCraftOrderCommand
                {
                    StationId = _stationId,
                    OrderId = orderId
                }))
                {
                    text = L10n.Tr("ui_cancel")
                };
                row.Add(cancel);
                _orderList.Add(row);
            }
        }

        private List<KeyValuePair<string, RecipeM1>> SortedRecipes()
        {
            var list = new List<KeyValuePair<string, RecipeM1>>();
            foreach (var pair in _world.Crafting.Recipes)
            {
                list.Add(pair);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }
    }
}
