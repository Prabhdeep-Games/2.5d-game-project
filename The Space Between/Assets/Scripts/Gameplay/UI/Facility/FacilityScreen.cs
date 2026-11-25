using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace TheSpaceBetween
{
    /// <summary>
    /// One screen of the Facility UI (pause, settings, credits, the main menu). The Facility UI keeps them in a stack:
    /// only the top one is shown and reads the menu input. Screens are built in code from the shared style sheet's classes.
    /// </summary>
    internal abstract class FacilityScreen
    {
        protected readonly FacilityUI ui;

        protected FacilityScreen(FacilityUI ui, string name, string screenClass)
        {
            this.ui = ui;
            Name = name;
            Root = new VisualElement { name = name };
            Root.AddToClassList("fui-screen");
            Root.AddToClassList(screenClass);
        }

        public string Name { get; }

        public VisualElement Root { get; }

        /// <summary>Called each time the screen comes to the top.</summary>
        public virtual void OnShow()
        {
        }

        /// <summary>Called once a frame while the screen is on top.</summary>
        public abstract void HandleInput(MenuInput input);

        /// <summary>What Escape or the east button does on this screen.</summary>
        public virtual void Back()
        {
        }

        /// <summary>The screen's list of options, or null when it has none.</summary>
        public virtual MenuList Menu => null;

        protected static Label Text(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        protected static VisualElement Box(string className)
        {
            var box = new VisualElement();
            box.AddToClassList(className);
            return box;
        }
    }

    /// <summary>
    /// A vertical list of options. The selected one is brighter, with a short ochre line beside it. Disabled options
    /// are dimmed and skipped. Works with the menu keys and with the mouse.
    /// </summary>
    internal sealed class MenuList
    {
        private sealed class Option
        {
            public VisualElement Row;
            public string Text;
            public Action Action;
            public Func<bool> Enabled;
            public bool IsEnabled => Enabled == null || Enabled();
        }

        private readonly FacilityUI ui;
        private readonly List<Option> options = new List<Option>();
        private int selected;

        public MenuList(FacilityUI ui, string className)
        {
            this.ui = ui;
            Root = new VisualElement();
            Root.AddToClassList("fui-menu");
            Root.AddToClassList(className);
        }

        public VisualElement Root { get; }

        public string SelectedText => selected >= 0 && selected < options.Count ? options[selected].Text : null;

        public IReadOnlyList<string> Texts
        {
            get
            {
                var texts = new List<string>();
                foreach (Option option in options)
                {
                    texts.Add(option.Text);
                }

                return texts;
            }
        }

        public void Add(string text, Action action, Func<bool> enabled = null)
        {
            var option = new Option { Text = text, Action = action, Enabled = enabled, Row = new VisualElement() };
            option.Row.AddToClassList("fui-option");
            var indicator = new VisualElement();
            indicator.AddToClassList("fui-indicator");
            var label = new Label(text.ToUpperInvariant());
            label.AddToClassList("fui-option-label");
            option.Row.Add(indicator);
            option.Row.Add(label);

            int index = options.Count;
            option.Row.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (option.IsEnabled && selected != index)
                {
                    Select(index, true);
                }
            });
            option.Row.RegisterCallback<ClickEvent>(_ =>
            {
                if (option.IsEnabled)
                {
                    Select(index, false);
                    Activate();
                }
            });

            options.Add(option);
            Root.Add(option.Row);
        }

        /// <summary>Updates the enabled states and moves the selection to the first enabled option.</summary>
        public void Reset()
        {
            selected = -1;
            for (int i = 0; i < options.Count; i++)
            {
                if (selected < 0 && options[i].IsEnabled)
                {
                    selected = i;
                }
            }

            Refresh();
        }

        public void HandleInput(MenuInput input)
        {
            if (input.Vertical != 0)
            {
                Move(input.Vertical);
            }
            else if (input.Confirm)
            {
                Activate();
            }
        }

        public void Activate()
        {
            if (selected < 0 || selected >= options.Count || !options[selected].IsEnabled)
            {
                return;
            }

            ui.PlayConfirm();
            options[selected].Action?.Invoke();
        }

        /// <summary>Chooses an option by its text, as a click or key would. Returns false when it is missing or disabled.</summary>
        public bool Choose(string text)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].Text, text, StringComparison.OrdinalIgnoreCase) && options[i].IsEnabled)
                {
                    Select(i, false);
                    Activate();
                    return true;
                }
            }

            return false;
        }

        public bool IsEnabled(string text)
        {
            foreach (Option option in options)
            {
                if (string.Equals(option.Text, text, StringComparison.OrdinalIgnoreCase))
                {
                    return option.IsEnabled;
                }
            }

            return false;
        }

        private void Move(int direction)
        {
            for (int step = 1; step <= options.Count; step++)
            {
                int next = ((selected + direction * step) % options.Count + options.Count) % options.Count;
                if (options[next].IsEnabled)
                {
                    Select(next, true);
                    return;
                }
            }
        }

        private void Select(int index, bool withSound)
        {
            selected = index;
            Refresh();
            if (withSound)
            {
                ui.PlayMove();
            }
        }

        private void Refresh()
        {
            for (int i = 0; i < options.Count; i++)
            {
                options[i].Row.EnableInClassList("is-selected", i == selected);
                options[i].Row.EnableInClassList("is-disabled", !options[i].IsEnabled);
            }
        }
    }
}
