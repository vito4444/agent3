using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Starsoil.Core;
using Starsoil.Data;

namespace Starsoil.UI
{
    /// <summary>
    /// Jobs panel (M2-T8): J key toggles; headcount +/- per job (quotas reassign in id
    /// order) and a live count of current assignments. The task-priority matrix editor
    /// ships with the full UI pass; matrix edits are already supported via commands.
    /// </summary>
    public sealed class JobsPanelController : MonoBehaviour
    {
        private World _world;
        private VisualElement _panel;
        private VisualElement _rows;
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
            doc.sortingOrder = 20;
            var root = doc.rootVisualElement;

            _panel = new VisualElement();
            _panel.style.position = Position.Absolute;
            _panel.style.left = 8;
            _panel.style.top = 60;
            _panel.style.width = 340;
            _panel.style.backgroundColor = new Color(0.08f, 0.12f, 0.1f, 0.96f);
            _panel.style.paddingLeft = 10;
            _panel.style.paddingRight = 10;
            _panel.style.paddingTop = 8;
            _panel.style.paddingBottom = 8;
            _panel.style.display = DisplayStyle.None;

            var title = new Label { text = L10n.Tr("ui_jobs") };
            title.style.color = Color.white;
            title.style.fontSize = 16;
            _panel.Add(title);

            _rows = new VisualElement();
            _panel.Add(_rows);
            root.Add(_panel);
            _uiReady = true;
        }

        private void Update()
        {
            if (!_uiReady || _world == null)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.J))
            {
                bool visible = _panel.style.display == DisplayStyle.Flex;
                _panel.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
                if (!visible)
                {
                    Rebuild();
                }
            }
        }

        private void Rebuild()
        {
            _rows.Clear();
            foreach (JobType job in Enum.GetValues(typeof(JobType)))
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 2;

                int current = 0;
                foreach (var colonist in _world.Colonists.AllSorted())
                {
                    if (colonist.Alive && colonist.Job == job)
                    {
                        current++;
                    }
                }
                _world.Jobs.Quotas.TryGetValue(job, out int quota);

                var label = new Label { text = L10n.Tr("job_" + job.ToString().ToLowerInvariant()) + "  " + current + " / " + quota };
                label.style.color = Color.white;
                label.style.flexGrow = 1f;
                row.Add(label);

                JobType captured = job;
                var minus = new Button(() => Adjust(captured, -1)) { text = "-" };
                var plus = new Button(() => Adjust(captured, +1)) { text = "+" };
                row.Add(minus);
                row.Add(plus);
                _rows.Add(row);
            }
        }

        private void Adjust(JobType job, int delta)
        {
            _world.Jobs.Quotas.TryGetValue(job, out int quota);
            var command = new SetJobQuotasCommand();
            command.Quotas.Add(new KeyValuePair<string, int>(job.ToString(), Math.Max(0, quota + delta)));
            _world.Commands.Enqueue(command);
            Rebuild();
        }
    }
}
