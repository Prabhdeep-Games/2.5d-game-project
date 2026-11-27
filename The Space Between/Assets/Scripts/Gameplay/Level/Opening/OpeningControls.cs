using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheSpaceBetween
{
    /// <summary>
    /// Which of the player's abilities the opening has handed over so far. Each ability is off until it is allowed: by the
    /// awakening for walking, or once the player passes an unlock point along X for running, sliding and jumping. Once
    /// allowed, an ability stays allowed, and a player who continues the game from a later room gets everything behind them.
    ///
    /// It works by switching the player's own input actions off and on, so the player controller itself is unchanged. The
    /// swap stays off for the whole opening unless Swap Allowed is on. Runs before the player each frame, so a key that is
    /// not allowed yet is never read, even in the frame the controller turns its actions back on.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class OpeningControls : MonoBehaviour
    {
        /// <summary>The abilities the opening teaches, in order.</summary>
        public enum Ability
        {
            Move,
            Run,
            Slide,
            Jump
        }

        /// <summary>An ability that becomes available once the player has passed a point.</summary>
        [Serializable]
        public class Unlock
        {
            public Ability ability;

            [Tooltip("The ability is allowed once the player's position passes this point along X.")]
            public Transform from;
        }

        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private PlaceholderPlayerController player;

        //==================== ABILITIES =====================
        [Header("Abilities")]
        [Tooltip("Where each ability becomes available. Walking is normally allowed by the awakening instead.")]
        [SerializeField] private List<Unlock> unlocks = new List<Unlock>();

        [Tooltip("Let the player swap during the opening. Off: the opening does not teach the swap.")]
        [SerializeField] private bool swapAllowed;

        //==================== DEBUG =====================
        [Header("Debug")]
        [ReadOnly, SerializeField] private bool moveAllowed;
        [ReadOnly, SerializeField] private bool runAllowed;
        [ReadOnly, SerializeField] private bool slideAllowed;
        [ReadOnly, SerializeField] private bool jumpAllowed;

        private SwapAbility swap;

        public PlaceholderPlayerController Player => player;

        public IReadOnlyList<Unlock> Unlocks => unlocks;

        /// <summary>Whether the opening has handed this ability to the player yet.</summary>
        public bool IsAllowed(Ability ability)
        {
            switch (ability)
            {
                case Ability.Move: return moveAllowed;
                case Ability.Run: return runAllowed;
                case Ability.Slide: return slideAllowed;
                default: return jumpAllowed;
            }
        }

        /// <summary>Hands an ability to the player. It stays allowed.</summary>
        public void Allow(Ability ability)
        {
            switch (ability)
            {
                case Ability.Move: moveAllowed = true; break;
                case Ability.Run: runAllowed = true; break;
                case Ability.Slide: slideAllowed = true; break;
                default: jumpAllowed = true; break;
            }

            Apply();
        }

        private void Awake()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlaceholderPlayerController>();
            }

            swap = player != null ? player.GetComponent<SwapAbility>() : null;
        }

        private void Start()
        {
            Apply();
        }

        private void Update()
        {
            if (player == null)
            {
                return;
            }

            float x = player.transform.position.x;
            foreach (Unlock unlock in unlocks)
            {
                if (unlock.from != null && !IsAllowed(unlock.ability) && x >= unlock.from.position.x)
                {
                    Allow(unlock.ability);
                }
            }

            Apply();
        }

        // The controller turns every action on whenever it is enabled, so this is checked every frame.
        private void Apply()
        {
            if (player == null)
            {
                return;
            }

            if (player.isActiveAndEnabled)
            {
                Set(player.MoveAction, moveAllowed);
                Set(player.RunAction, runAllowed);
                Set(player.SlideAction, slideAllowed);
                Set(player.JumpAction, jumpAllowed);
            }

            if (swap != null && !swapAllowed && swap.SwapAction != null && swap.SwapAction.enabled)
            {
                swap.SwapAction.Disable();
            }
        }

        private static void Set(InputAction action, bool allowed)
        {
            if (action == null || action.enabled == allowed)
            {
                return;
            }

            if (allowed)
            {
                action.Enable();
            }
            else
            {
                action.Disable();
            }
        }
    }
}
