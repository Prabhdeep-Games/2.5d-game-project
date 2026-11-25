using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheSpaceBetween
{
    /// <summary>The pause menu: a narrow panel on the left, so the paused level stays in view behind it.</summary>
    internal sealed class PauseScreen : FacilityScreen
    {
        public const string ScreenName = "Pause";

        public PauseScreen(FacilityUI ui) : base(ui, ScreenName, "fui-pause")
        {
            VisualElement panel = Box("fui-pause-panel");
            panel.Add(Text("PAUSED", "fui-heading"));
            panel.Add(Box("fui-rule"));

            menu = new MenuList(ui, "fui-pause-menu");
            Menu.Add("Resume", ui.Resume);
            Menu.Add("Restart Checkpoint", ui.RestartCheckpoint);
            Menu.Add("Settings", ui.OpenSettings);
            Menu.Add("Main Menu", ui.GoToMainMenu);
            panel.Add(Menu.Root);
            Root.Add(panel);
        }

        private readonly MenuList menu;

        public override MenuList Menu => menu;

        public override void OnShow()
        {
            Menu.Reset();
        }

        public override void Back()
        {
            ui.PlayBack();
            ui.Resume();
        }

        public override void HandleInput(MenuInput input)
        {
            if (input.Back || input.Pause)
            {
                Back();
                return;
            }

            Menu.HandleInput(input);
        }
    }

    /// <summary>
    /// The credits: a slow list scrolling up over a darkened view. The names come from the Facility UI's Credits list;
    /// while it is empty, only the heading and the note are shown. Any confirm or back key ends it early.
    /// </summary>
    internal sealed class CreditsScreen : FacilityScreen
    {
        public const string ScreenName = "Credits";

        // Where the top of the list starts, as a share of the screen height from the top.
        private const float StartHeight = 0.7f;

        private readonly VisualElement column;
        private readonly float speed;
        private readonly Action finished;
        private float offset;
        private bool done;

        public CreditsScreen(FacilityUI ui, string heading, IReadOnlyList<string> lines, string emptyNote, float speed, Action finished)
            : base(ui, ScreenName, "fui-credits")
        {
            this.speed = speed;
            this.finished = finished;
            column = Box("fui-credits-column");
            column.Add(Text(heading.ToUpperInvariant(), "fui-credits-heading"));

            bool any = false;
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    column.Add(Box("fui-credits-gap"));
                    continue;
                }

                any = true;

                // "Role: Name" shows the role small and muted above the name.
                int colon = line.IndexOf(':');
                if (colon > 0)
                {
                    column.Add(Text(line.Substring(0, colon).Trim().ToUpperInvariant(), "fui-credits-role"));
                    column.Add(Text(line.Substring(colon + 1).Trim(), "fui-credits-name"));
                }
                else
                {
                    column.Add(Text(line, "fui-credits-name"));
                }
            }

            if (!any && !string.IsNullOrEmpty(emptyNote))
            {
                column.Add(Text(emptyNote.ToUpperInvariant(), "fui-credits-role"));
            }

            Root.Add(column);
        }

        public bool IsFinished => done;

        public override void OnShow()
        {
            offset = 0f;
            done = false;
        }

        public override void HandleInput(MenuInput input)
        {
            if (done)
            {
                return;
            }

            if (input.Back || input.Confirm)
            {
                Back();
                return;
            }

            // The column starts in the lower part of the screen and rolls up until its end has left the top.
            float start = Root.layout.height * StartHeight;
            float columnHeight = column.layout.height;
            if (float.IsNaN(start) || start <= 0f)
            {
                return;
            }

            offset += speed * Time.unscaledDeltaTime;
            column.style.translate = new Translate(0f, start - offset, 0f);
            if (offset > start + columnHeight)
            {
                Finish();
            }
        }

        public override void Back() => Finish();

        private void Finish()
        {
            done = true;
            finished?.Invoke();
        }
    }
}
