using UnityEngine;
using UnityEngine.UIElements;
using Starsoil.Core;
using Starsoil.Data;

namespace Starsoil.UI
{
    /// <summary>
    /// Trade &amp; diplomacy panel (M6-T6, docs/plan/07). G key toggles. Sections:
    /// live quote board with accept buttons, 行情雷达 previews of the next 3 boards
    /// (branch_orbital_logistics_3), and the Red Banner ultimatum with the pay-off
    /// path (缓和路径). Reads the universe directly like StarMapController.
    /// </summary>
    public sealed class TradePanelController : MonoBehaviour
    {
        private const int PreviewBoards = 3;

        private Universe _universe;
        private VisualElement _panel;
        private ScrollView _list;
        private bool _uiReady;
        private long _lastRefreshTick = -1;

        public void Init(Universe universe)
        {
            _universe = universe;
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
            doc.sortingOrder = 42;
            var root = doc.rootVisualElement;

            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.left = Length.Percent(28f);
            _panel.style.right = Length.Percent(28f);
            _panel.style.top = 60;
            _panel.style.maxHeight = 620;
            _panel.style.backgroundColor = new Color(0.05f, 0.08f, 0.1f, 0.97f);
            _panel.style.paddingLeft = 12;
            _panel.style.paddingRight = 12;
            _panel.style.paddingTop = 8;
            _panel.style.paddingBottom = 8;
            _panel.style.display = DisplayStyle.None;

            var title = new Label { text = L10n.Tr("ui_trade_panel") };
            title.style.color = Color.white;
            title.style.fontSize = 16;
            _panel.Add(title);

            _list = new ScrollView();
            _list.style.maxHeight = 560;
            _panel.Add(_list);
            root.Add(_panel);
            _uiReady = true;
        }

        /// <summary>Programmatic open/close (demo driver, docs/plan/05 键位).</summary>
        public void Toggle()
        {
            bool visible = _panel.style.display == DisplayStyle.Flex;
            _panel.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            _lastRefreshTick = -1;
        }

        private void Update()
        {
            if (!_uiReady || _universe == null)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.G))
            {
                Toggle();
            }
            if (_panel.style.display != DisplayStyle.Flex)
            {
                return;
            }
            if (_lastRefreshTick < 0 ||
                (_universe.Tick != _lastRefreshTick && _universe.Tick % GameConstants.TicksPerHour == 0))
            {
                Rebuild();
            }
        }

        private void Rebuild()
        {
            _lastRefreshTick = _universe.Tick;
            _list.Clear();

            var credits = new Label { text = L10n.Tr("ui_credits") + ": " + _universe.PlayerCredits.ToString("0") };
            credits.style.color = new Color(0.95f, 0.85f, 0.4f);
            _list.Add(credits);

            AddQuoteSection();
            AddRadarSection();
            AddUltimatumSection();
        }

        private void AddQuoteSection()
        {
            var header = new Label { text = L10n.Tr("ui_quote_board") };
            header.style.color = new Color(0.7f, 0.85f, 0.95f);
            header.style.marginTop = 6;
            _list.Add(header);

            var quotes = _universe.FactionsSandbox.Quotes;
            if (quotes.Count == 0)
            {
                _list.Add(Dim(L10n.Tr("ui_no_quotes")));
                return;
            }
            long hour = _universe.Tick / GameConstants.TicksPerHour;
            foreach (var quote in quotes)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;
                row.style.marginBottom = 2;

                string direction = L10n.Tr(quote.MerchantSells ? "ui_quote_sell" : "ui_quote_buy");
                var text = new Label
                {
                    text = direction + " " + ItemCatalog.NameOf(quote.ItemId) + " ×" + quote.Count +
                           " @ " + quote.UnitPrice.ToString("0.##") +
                           "  (" + (quote.ExpiresHour - hour) + "h)"
                };
                text.style.color = quote.MerchantSells ? new Color(0.85f, 0.75f, 0.6f) : new Color(0.6f, 0.85f, 0.65f);
                row.Add(text);

                int quoteId = quote.Id;
                var accept = new Button(() =>
                {
                    _universe.FactionsSandbox.AcceptQuote(_universe, quoteId);
                    _lastRefreshTick = -1;
                })
                { text = L10n.Tr("ui_accept") };
                row.Add(accept);
                _list.Add(row);
            }
        }

        private void AddRadarSection()
        {
            var preview = _universe.FactionsSandbox.PeekUpcomingQuotes(_universe, PreviewBoards);
            if (preview.Count == 0)
            {
                return; // 行情雷达 locked: section hidden entirely.
            }
            var header = new Label { text = L10n.Tr("ui_upcoming_quotes") };
            header.style.color = new Color(0.7f, 0.85f, 0.95f);
            header.style.marginTop = 6;
            _list.Add(header);

            long boardExpires = -1;
            foreach (var quote in preview)
            {
                if (quote.ExpiresHour != boardExpires)
                {
                    boardExpires = quote.ExpiresHour;
                    var boardLabel = Dim(L10n.Tr("ui_board_at_hour") + " " + boardExpires);
                    boardLabel.style.marginTop = 3;
                    _list.Add(boardLabel);
                }
                string direction = L10n.Tr(quote.MerchantSells ? "ui_quote_sell" : "ui_quote_buy");
                _list.Add(Dim("  " + direction + " " + ItemCatalog.NameOf(quote.ItemId) + " ×" + quote.Count +
                              " @ " + quote.UnitPrice.ToString("0.##")));
            }
        }

        private void AddUltimatumSection()
        {
            int demand = _universe.FactionsSandbox.UltimatumDemand();
            if (demand <= 0)
            {
                return;
            }
            var header = new Label { text = L10n.Tr("ui_ultimatum") };
            header.style.color = new Color(0.95f, 0.5f, 0.45f);
            header.style.marginTop = 6;
            _list.Add(header);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.Add(Dim(L10n.Tr("ui_ultimatum_demand") + ": " + demand));
            var pay = new Button(() =>
            {
                _universe.FactionsSandbox.PayUltimatum(_universe);
                _lastRefreshTick = -1;
            })
            { text = L10n.Tr("ui_pay_ultimatum") };
            row.Add(pay);
            _list.Add(row);
        }

        private static Label Dim(string text)
        {
            var label = new Label { text = text };
            label.style.color = new Color(0.75f, 0.78f, 0.8f);
            return label;
        }
    }
}
