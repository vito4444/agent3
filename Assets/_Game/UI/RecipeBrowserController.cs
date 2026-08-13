using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Starsoil.Core;
using Starsoil.Data;

namespace Starsoil.UI
{
    /// <summary>
    /// Recipe browser (M3-T5): R key toggles; text search over names and rationale,
    /// station filter buttons, and per-recipe upstream (inputs) / downstream (what
    /// consumes the output) expansion. Locked recipes show as outlines with their tech
    /// node. Any recipe is reachable in ≤3 clicks: open → (filter/search) → row.
    /// </summary>
    public sealed class RecipeBrowserController : MonoBehaviour
    {
        private World _world;
        private VisualElement _panel;
        private TextField _search;
        private ScrollView _list;
        private string _stationFilter = string.Empty;
        private bool _uiReady;

        public void Init(World world)
        {
            _world = world;
            BuildUi();
        }

        public void SwitchWorld(World world)
        {
            _world = world;
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
            doc.sortingOrder = 30;
            var root = doc.rootVisualElement;

            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.left = Length.Percent(20f);
            _panel.style.right = Length.Percent(20f);
            _panel.style.top = 50;
            _panel.style.maxHeight = 680;
            _panel.style.backgroundColor = new Color(0.07f, 0.08f, 0.12f, 0.97f);
            _panel.style.paddingLeft = 12;
            _panel.style.paddingRight = 12;
            _panel.style.paddingTop = 8;
            _panel.style.paddingBottom = 8;
            _panel.style.display = DisplayStyle.None;

            var title = new Label { text = L10n.Tr("ui_recipe_browser") };
            title.style.color = Color.white;
            title.style.fontSize = 16;
            _panel.Add(title);

            _search = new TextField();
            _search.RegisterValueChangedCallback(_ => Rebuild());
            _panel.Add(_search);

            var filters = new VisualElement();
            filters.style.flexDirection = FlexDirection.Row;
            filters.style.flexWrap = Wrap.Wrap;
            filters.style.flexShrink = 0f;
            filters.style.marginBottom = 4;
            AddFilter(filters, string.Empty, L10n.Tr("ui_filter_all"));
            foreach (var kind in new[]
                     {
                         BuildingKind.Workbench, BuildingKind.Campfire, BuildingKind.Furnace,
                         BuildingKind.Crusher, BuildingKind.Press, BuildingKind.Assembler,
                         BuildingKind.Distiller, BuildingKind.ChemReactor, BuildingKind.PolymerReactor,
                         BuildingKind.CryoLiquefier, BuildingKind.CultureVat
                     })
            {
                AddFilter(filters, kind.ToString(), StationName(kind.ToString()));
            }
            _panel.Add(filters);

            _list = new ScrollView();
            _list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _list.style.flexGrow = 1f;
            _list.style.maxHeight = 540;
            _panel.Add(_list);

            root.Add(_panel);
            _uiReady = true;
        }

        /// <summary>Localized station label (station_&lt;BuildingKind&gt; keys).</summary>
        private static string StationName(string kind)
        {
            return L10n.Tr("station_" + kind);
        }

        private void AddFilter(VisualElement parent, string kind, string label)
        {
            var button = new Button(() =>
            {
                _stationFilter = kind;
                Rebuild();
            })
            { text = label };
            parent.Add(button);
        }

        /// <summary>Programmatic open/close (demo driver, docs/plan/05 键位).</summary>
        public void Toggle()
        {
            bool visible = _panel.style.display == DisplayStyle.Flex;
            _panel.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            if (!visible)
            {
                Rebuild();
            }
        }

        private void Update()
        {
            if (!_uiReady || _world == null)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.F4))
            {
                Toggle();
            }
        }

        private void Rebuild()
        {
            _list.Clear();
            string query = (_search.value ?? string.Empty).ToLowerInvariant();
            int shown = 0;
            foreach (var pair in SortedRecipes())
            {
                var recipe = pair.Value;
                if (_stationFilter.Length > 0 && recipe.Station.ToString() != _stationFilter &&
                    recipe.HandStation.ToString() != _stationFilter)
                {
                    continue;
                }
                if (query.Length > 0 && !Matches(recipe, query))
                {
                    continue;
                }
                _list.Add(BuildRow(recipe));
                shown++;
                if (shown >= 120)
                {
                    break;
                }
            }
        }

        private bool Matches(RecipeM1 recipe, string query)
        {
            if (recipe.Id.Contains(query) || recipe.RationaleZh.Contains(query) ||
                recipe.RationaleEn.ToLowerInvariant().Contains(query))
            {
                return true;
            }
            foreach (var output in recipe.Outputs)
            {
                if (ItemCatalog.NameOf(output.ItemId).ToLowerInvariant().Contains(query))
                {
                    return true;
                }
            }
            return false;
        }

        private VisualElement BuildRow(RecipeM1 recipe)
        {
            bool unlocked = _world.Tech.IsRecipeUnlocked(recipe.Id);
            var row = new VisualElement();
            row.style.backgroundColor = new Color(0.12f, 0.14f, 0.19f, unlocked ? 0.95f : 0.55f);
            row.style.marginBottom = 3;
            row.style.paddingLeft = 6;
            row.style.paddingRight = 6;
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;

            string outputs = Join(recipe.Outputs);
            string header = outputs + "  ←  " + Join(recipe.Inputs) +
                            "   [" + StationName(recipe.Station.ToString()) + (recipe.HasHandStation ? "/" + StationName(recipe.HandStation.ToString()) : "") + "]";
            var head = new Label { text = (unlocked ? "" : "🔒 ") + header };
            head.style.color = unlocked ? Color.white : new Color(0.6f, 0.6f, 0.65f);
            head.style.whiteSpace = WhiteSpace.Normal;
            row.Add(head);

            if (unlocked)
            {
                var rationale = new Label { text = L10n.Language == "en" ? recipe.RationaleEn : recipe.RationaleZh };
                rationale.style.color = new Color(0.72f, 0.76f, 0.8f);
                rationale.style.fontSize = 11;
                rationale.style.whiteSpace = WhiteSpace.Normal;
                row.Add(rationale);

                var buttons = new VisualElement();
                buttons.style.flexDirection = FlexDirection.Row;
                string firstOutput = recipe.Outputs.Count > 0 ? recipe.Outputs[0].ItemId : string.Empty;
                var upstream = new Button(() => ShowRelated(recipe, upstream: true)) { text = L10n.Tr("ui_upstream") };
                var downstream = new Button(() => ShowRelated(recipe, upstream: false)) { text = L10n.Tr("ui_downstream") };
                buttons.Add(upstream);
                buttons.Add(downstream);
                row.Add(buttons);
            }
            return row;
        }

        /// <summary>Upstream = search producers of this recipe's inputs; downstream =
        /// search consumers of its outputs (one click each, M3-T5).</summary>
        private void ShowRelated(RecipeM1 recipe, bool upstream)
        {
            var targets = new HashSet<string>();
            foreach (var ing in upstream ? recipe.Inputs : recipe.Outputs)
            {
                targets.Add(ing.ItemId);
            }
            _list.Clear();
            foreach (var pair in SortedRecipes())
            {
                var candidate = pair.Value;
                var side = upstream ? candidate.Outputs : candidate.Inputs;
                foreach (var ing in side)
                {
                    if (targets.Contains(ing.ItemId))
                    {
                        _list.Add(BuildRow(candidate));
                        break;
                    }
                }
            }
        }

        private static string Join(List<Ingredient> ingredients)
        {
            var parts = new List<string>();
            foreach (var ing in ingredients)
            {
                parts.Add(ItemCatalog.NameOf(ing.ItemId) + "×" + ing.Count);
            }
            return string.Join(" + ", parts);
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
