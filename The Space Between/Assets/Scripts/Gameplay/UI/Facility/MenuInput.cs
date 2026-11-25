using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheSpaceBetween
{
    /// <summary>
    /// The keys and buttons that move through the menus: arrows or W/S/A/D, the D-pad or left stick, Enter, Space or
    /// the south button to choose, Escape, Backspace or the east button to go back, and Escape or Start to pause.
    /// Read on real time, so it works while the game is paused. Holding a direction repeats it.
    /// </summary>
    internal sealed class MenuInput : IDisposable
    {
        private const float RepeatDelay = 0.4f;
        private const float RepeatInterval = 0.12f;

        private readonly InputAction up = Button("Menu Up", "<Keyboard>/upArrow", "<Keyboard>/w", "<Gamepad>/dpad/up", "<Gamepad>/leftStick/up");
        private readonly InputAction down = Button("Menu Down", "<Keyboard>/downArrow", "<Keyboard>/s", "<Gamepad>/dpad/down", "<Gamepad>/leftStick/down");
        private readonly InputAction left = Button("Menu Left", "<Keyboard>/leftArrow", "<Keyboard>/a", "<Gamepad>/dpad/left", "<Gamepad>/leftStick/left");
        private readonly InputAction right = Button("Menu Right", "<Keyboard>/rightArrow", "<Keyboard>/d", "<Gamepad>/dpad/right", "<Gamepad>/leftStick/right");
        private readonly InputAction confirm = Button("Menu Confirm", "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space", "<Gamepad>/buttonSouth");
        private readonly InputAction back = Button("Menu Back", "<Keyboard>/escape", "<Keyboard>/backspace", "<Gamepad>/buttonEast");
        private readonly InputAction pause = Button("Pause", "<Keyboard>/escape", "<Gamepad>/start");

        private float nextRepeat;
        private int heldDirection;

        /// <summary>-1 up, 1 down, 0 neither, this frame (with repeat while held).</summary>
        public int Vertical { get; private set; }

        /// <summary>-1 left, 1 right, 0 neither, this frame (with repeat while held).</summary>
        public int Horizontal { get; private set; }

        public bool Confirm { get; private set; }
        public bool Back { get; private set; }
        public bool Pause { get; private set; }

        public MenuInput()
        {
            foreach (InputAction action in All())
            {
                action.Enable();
            }
        }

        private static InputAction Button(string name, params string[] paths)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (string path in paths)
            {
                action.AddBinding(path);
            }

            return action;
        }

        private InputAction[] All() => new[] { up, down, left, right, confirm, back, pause };

        /// <summary>Reads this frame's input. Call once a frame.</summary>
        public void Update()
        {
            Confirm = confirm.WasPressedThisFrame();
            Back = back.WasPressedThisFrame();
            Pause = pause.WasPressedThisFrame();

            int direction = up.IsPressed() ? 1 : down.IsPressed() ? 2 : left.IsPressed() ? 3 : right.IsPressed() ? 4 : 0;
            bool fire = false;
            float now = Time.unscaledTime;
            if (direction != heldDirection)
            {
                heldDirection = direction;
                nextRepeat = now + RepeatDelay;
                fire = direction != 0;
            }
            else if (direction != 0 && now >= nextRepeat)
            {
                nextRepeat = now + RepeatInterval;
                fire = true;
            }

            Vertical = fire && direction == 1 ? -1 : fire && direction == 2 ? 1 : 0;
            Horizontal = fire && direction == 3 ? -1 : fire && direction == 4 ? 1 : 0;
        }

        /// <summary>Forgets this frame's presses, so one key press is not handled twice.</summary>
        public void Consume()
        {
            Confirm = Back = Pause = false;
            Vertical = Horizontal = 0;
        }

        public void Dispose()
        {
            foreach (InputAction action in All())
            {
                action.Dispose();
            }
        }
    }
}
