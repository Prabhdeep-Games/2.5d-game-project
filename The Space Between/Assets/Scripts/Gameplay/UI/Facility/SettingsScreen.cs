using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TheSpaceBetween
{
    /// <summary>
    /// Display and audio settings on the left and the controls on the right. Every value is read from and written to
    /// GameSettings, which applies it at once. The controls are shown, not changed: they are read from the real input
    /// actions, so they always match the game. Up and down choose a row, left and right change it.
    /// </summary>
    internal sealed class SettingsScreen : FacilityScreen
    {
        public const string ScreenName = "Settings";
        private const float VolumeStep = 0.05f;

        private abstract class Row
        {
            public VisualElement Root;
            public abstract string Name { get; }
            public abstract void Step(int direction);
            public virtual void Activate() => Step(1);
            public abstract void Refresh();
        }

        private sealed class StepperRow : Row
        {
            private readonly string name;
            private readonly Func<string[]> values;
            private readonly Func<int> get;
            private readonly Action<int> set;
            private readonly Func<string> fallback;
            private readonly Label value;

            public StepperRow(string name, Func<string[]> values, Func<int> get, Action<int> set, Func<string> fallback = null)
            {
                this.fallback = fallback;
                this.name = name;
                this.values = values;
                this.get = get;
                this.set = set;
                Root = NewRow(name);
                VisualElement control = Box("fui-stepper");
                Label previous = Text("‹", "fui-stepper-arrow");
                value = Text(string.Empty, "fui-value");
                Label next = Text("›", "fui-stepper-arrow");
                previous.RegisterCallback<ClickEvent>(_ => Step(-1));
                next.RegisterCallback<ClickEvent>(_ => Step(1));
                control.Add(previous);
                control.Add(value);
                control.Add(next);
                Root.Add(control);
            }

            public override string Name => name;

            public override void Step(int direction)
            {
                string[] all = values();
                if (all.Length > 0)
                {
                    set(Mathf.Clamp(get() + direction, 0, all.Length - 1));
                }

                Refresh();
            }

            public override void Refresh()
            {
                string[] all = values();
                int index = get();
                value.text = index >= 0 && index < all.Length ? all[index] : fallback != null ? fallback() : string.Empty;
            }
        }

        private sealed class ToggleRow : Row
        {
            private readonly string name;
            private readonly Func<bool> get;
            private readonly Action<bool> set;
            private readonly VisualElement box;
            private readonly Label state;

            public ToggleRow(string name, Func<bool> get, Action<bool> set)
            {
                this.name = name;
                this.get = get;
                this.set = set;
                Root = NewRow(name);
                VisualElement control = Box("fui-toggle");
                box = Box("fui-toggle-box");
                box.Add(Box("fui-toggle-fill"));
                state = Text(string.Empty, "fui-value");
                control.Add(box);
                control.Add(state);
                control.RegisterCallback<ClickEvent>(_ => Activate());
                Root.Add(control);
            }

            public override string Name => name;

            public override void Step(int direction) => Activate();

            public override void Activate()
            {
                set(!get());
                Refresh();
            }

            public override void Refresh()
            {
                bool on = get();
                box.EnableInClassList("is-on", on);
                state.text = on ? "ON" : "OFF";
            }
        }

        private sealed class SliderRow : Row
        {
            private readonly string name;
            private readonly Func<float> get;
            private readonly Action<float> set;
            private readonly VisualElement track;
            private readonly VisualElement fill;
            private readonly Label value;

            public SliderRow(string name, Func<float> get, Action<float> set)
            {
                this.name = name;
                this.get = get;
                this.set = set;
                Root = NewRow(name);
                VisualElement control = Box("fui-slider");
                // A tall, invisible track to click on, holding the thin visible line and its ochre fill.
                track = Box("fui-slider-track");
                VisualElement line = Box("fui-slider-line");
                fill = Box("fui-slider-fill");
                line.Add(fill);
                track.Add(line);
                value = Text(string.Empty, "fui-value");
                control.Add(track);
                control.Add(value);
                Root.Add(control);

                // Clicking or dragging along the track sets the value at the pointer.
                track.RegisterCallback<PointerDownEvent>(e =>
                {
                    track.CapturePointer(e.pointerId);
                    SetFromPointer(e.localPosition.x);
                });
                track.RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (track.HasPointerCapture(e.pointerId))
                    {
                        SetFromPointer(e.localPosition.x);
                    }
                });
                track.RegisterCallback<PointerUpEvent>(e => track.ReleasePointer(e.pointerId));
            }

            public override string Name => name;

            public override void Step(int direction)
            {
                set(Mathf.Round((get() + direction * VolumeStep) / VolumeStep) * VolumeStep);
                Refresh();
            }

            public override void Activate()
            {
            }

            public override void Refresh()
            {
                float v = Mathf.Clamp01(get());
                fill.style.width = Length.Percent(v * 100f);
                value.text = Mathf.RoundToInt(v * 100f).ToString();
            }

            private void SetFromPointer(float x)
            {
                float width = track.layout.width;
                if (width > 0f)
                {
                    set(Mathf.Clamp01(x / width));
                    Refresh();
                }
            }
        }

        private sealed class ButtonRow : Row
        {
            private readonly string name;
            private readonly Action action;

            public ButtonRow(string name, Action action)
            {
                this.name = name;
                this.action = action;
                Root = new VisualElement();
                Root.AddToClassList("fui-option");
                Root.Add(Box("fui-indicator"));
                Root.Add(Text(name.ToUpperInvariant(), "fui-option-label"));
                Root.RegisterCallback<ClickEvent>(_ => Activate());
            }

            public override string Name => name;

            public override void Step(int direction)
            {
            }

            public override void Activate() => action();

            public override void Refresh()
            {
            }
        }

        private readonly List<Row> rows = new List<Row>();
        private readonly List<(Label keyboard, Label gamepad, Func<TutorialGuide.InputDevice, string> read)> controls =
            new List<(Label, Label, Func<TutorialGuide.InputDevice, string>)>();
        private Resolution[] resolutions = new Resolution[0];
        private string[] resolutionNames = new string[0];
        private int selected;

        public SettingsScreen(FacilityUI ui) : base(ui, ScreenName, "fui-settings")
        {
            VisualElement page = Box("fui-settings-page");
            page.Add(Text("SETTINGS", "fui-heading"));
            page.Add(Box("fui-rule"));

            VisualElement columns = Box("fui-settings-columns");
            VisualElement left = Box("fui-settings-column");
            VisualElement right = Box("fui-settings-column");
            columns.Add(left);
            columns.Add(right);
            page.Add(columns);

            left.Add(Text("DISPLAY", "fui-section"));
            AddRow(left, new StepperRow("Resolution", () => resolutionNames, ResolutionIndex, SetResolution, () => $"{GameSettings.Width} × {GameSettings.Height}"));
            AddRow(left, new ToggleRow("Fullscreen", () => GameSettings.Fullscreen, v => GameSettings.Fullscreen = v));
            AddRow(left, new ToggleRow("VSync", () => GameSettings.VSync, v => GameSettings.VSync = v));
            AddRow(left, new StepperRow("Graphics Quality", QualityNames, () => GameSettings.Quality, v => GameSettings.Quality = v));

            left.Add(Text("AUDIO", "fui-section"));
            AddRow(left, new SliderRow("Master Volume", () => GameSettings.Master, v => GameSettings.Master = v));
            AddRow(left, new SliderRow("Sound Effects", () => GameSettings.Volume(AudioChannelKind.Effects), v => GameSettings.SetVolume(AudioChannelKind.Effects, v)));
            AddRow(left, new SliderRow("Ambience", () => GameSettings.Volume(AudioChannelKind.Ambience), v => GameSettings.SetVolume(AudioChannelKind.Ambience, v)));
            AddRow(left, new SliderRow("Music", () => GameSettings.Volume(AudioChannelKind.Music), v => GameSettings.SetVolume(AudioChannelKind.Music, v)));

            right.Add(Text("CONTROLS", "fui-section"));
            VisualElement header = NewRow(string.Empty);
            header.AddToClassList("fui-controls-header");
            header.Add(Text("KEYBOARD AND MOUSE", "fui-control-device"));
            header.Add(Text("GAMEPAD", "fui-control-device"));
            right.Add(header);
            AddControl(right, "Movement", d => TutorialGuide.CompositeLabel(ui.Controls.Player?.MoveAction, d, null));
            AddControl(right, "Jump", d => TutorialGuide.Label(ui.Controls.Player?.JumpAction, d));
            AddControl(right, "Swap", d => TutorialGuide.Label(ui.Controls.Swap?.SwapAction, d));
            AddControl(right, "Aim", d => d == TutorialGuide.InputDevice.Gamepad
                ? TutorialGuide.Label(ui.Controls.Swap?.AimStickAction, d)
                : TutorialGuide.Label(ui.Controls.Swap?.PointerAction, d));
            AddControl(right, "Slide", d => TutorialGuide.Label(ui.Controls.Player?.SlideAction, d));
            AddControl(right, "Wall Run", d => TutorialGuide.Label(ui.Controls.Player?.RunAction, d) + " + " + TutorialGuide.CompositeLabel(ui.Controls.Player?.MoveAction, d, null));
            AddControl(right, "Observe", d => TutorialGuide.Label(ui.Controls.Observe?.ObserveAction, d));
            right.Add(Text("Wall run: hold run and move into a curved wall. Controls can't be changed yet.", "fui-note"));

            var back = new ButtonRow("Back", Close);
            rows.Add(back);
            page.Add(back.Root);
            back.Root.AddToClassList("fui-settings-back");
            int backIndex = rows.Count - 1;
            back.Root.RegisterCallback<PointerEnterEvent>(_ => Select(backIndex, true));

            Root.Add(page);
        }

        public string SelectedRow => selected >= 0 && selected < rows.Count ? rows[selected].Name : null;

        /// <summary>The text a control row shows for a device, for checks.</summary>
        public string ControlText(string action, bool gamepad)
        {
            foreach (var control in controls)
            {
                if (string.Equals(control.keyboard.parent.Q<Label>(className: "fui-row-label").text, action, StringComparison.OrdinalIgnoreCase))
                {
                    return gamepad ? control.gamepad.text : control.keyboard.text;
                }
            }

            return null;
        }

        /// <summary>Changes a row as left (-1) or right (1) would, for checks and for the mouse.</summary>
        public void StepRow(string name, int direction)
        {
            foreach (Row row in rows)
            {
                if (string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    row.Step(direction);
                }
            }
        }

        public override void OnShow()
        {
            resolutions = Screen.resolutions;
            var names = new List<string>();
            var unique = new List<Resolution>();
            foreach (Resolution r in resolutions)
            {
                string label = $"{r.width} × {r.height}";
                if (!names.Contains(label))
                {
                    names.Add(label);
                    unique.Add(r);
                }
            }

            resolutions = unique.ToArray();
            resolutionNames = names.ToArray();
            foreach (Row row in rows)
            {
                row.Refresh();
            }

            foreach (var control in controls)
            {
                control.keyboard.text = control.read(TutorialGuide.InputDevice.KeyboardMouse).ToUpperInvariant();
                control.gamepad.text = control.read(TutorialGuide.InputDevice.Gamepad).ToUpperInvariant();
            }

            Select(0, false);
        }

        public override void HandleInput(MenuInput input)
        {
            if (input.Back)
            {
                Back();
                return;
            }

            if (input.Vertical != 0)
            {
                Select((selected + input.Vertical + rows.Count) % rows.Count, true);
            }
            else if (input.Horizontal != 0)
            {
                rows[selected].Step(input.Horizontal);
                ui.PlayValue(input.Horizontal > 0);
            }
            else if (input.Confirm)
            {
                rows[selected].Activate();
                ui.PlayConfirm();
            }
        }

        public override void Back() => Close();

        private void Close()
        {
            GameSettings.Save();
            ui.PlayBack();
            ui.CloseScreen(this);
        }

        private void AddRow(VisualElement parent, Row row)
        {
            int index = rows.Count;
            rows.Add(row);
            row.Root.RegisterCallback<PointerEnterEvent>(_ => Select(index, true));
            parent.Add(row.Root);
        }

        private void AddControl(VisualElement parent, string name, Func<TutorialGuide.InputDevice, string> read)
        {
            VisualElement row = NewRow(name);
            row.AddToClassList("fui-control-row");
            Label keyboard = Text(string.Empty, "fui-control-key");
            Label gamepad = Text(string.Empty, "fui-control-key");
            row.Add(keyboard);
            row.Add(gamepad);
            controls.Add((keyboard, gamepad, read));
            parent.Add(row);
        }

        private void Select(int index, bool withSound)
        {
            if (withSound && index != selected)
            {
                ui.PlayMove();
            }

            selected = index;
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Root.EnableInClassList("is-selected", i == selected);
            }
        }

        private static VisualElement NewRow(string name)
        {
            var row = new VisualElement();
            row.AddToClassList("fui-row");
            row.Add(Box("fui-indicator"));
            row.Add(Text(name.ToUpperInvariant(), "fui-row-label"));
            return row;
        }

        private static string[] QualityNames()
        {
            string[] names = QualitySettings.names;
            var upper = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                upper[i] = names[i].ToUpperInvariant();
            }

            return upper;
        }

        private int ResolutionIndex()
        {
            for (int i = 0; i < resolutions.Length; i++)
            {
                if (resolutions[i].width == GameSettings.Width && resolutions[i].height == GameSettings.Height)
                {
                    return i;
                }
            }

            return -1;
        }

        private void SetResolution(int index)
        {
            if (index >= 0 && index < resolutions.Length)
            {
                GameSettings.SetResolution(resolutions[index].width, resolutions[index].height);
            }
        }
    }
}
