using System;
using UnityEngine;
using UnityEngine.Events;

namespace TheSpaceBetween
{
    /// <summary>
    /// Runs a priority-ordered list of State components.
    ///
    /// Every frame the list is checked from top to bottom. The first enabled state whose CanRun
    /// returns true becomes the active state. When the active state changes, OnExit is called on
    /// the old state and OnEnter on the new one, and then Tick is called on the active state.
    ///
    /// Put the most demanding state at the top of the list and an always-eligible fallback
    /// (for example an idle state whose CanRun returns true) at the bottom.
    /// </summary>
    public class StateMachine : MonoBehaviour
    {
        private const string NoStateLabel = "None";

        //==================== CONFIGURATION =====================
        [Header("States")]
        [Tooltip("Checked from top to bottom every frame. The first enabled state whose CanRun returns true becomes active. Drag State components here in priority order.")]
        [SerializeField] private State[] states = Array.Empty<State>();

        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked whenever the active state changes. Use it for effects, sounds, or animation triggers.")]
        [SerializeField] private UnityEvent stateChangedEvent;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write a message to the Console whenever the active state changes.")]
        [SerializeField] private bool logTransitions;

        [Tooltip("Type name of the state that is running now.")]
        [ReadOnly, SerializeField] private string currentStateName = NoStateLabel;

        private State current;
        private State previous;

        //==================== OUTPUTS =====================
        /// <summary>Raised when the active state changes. Arguments are the old and new state; either may be null.</summary>
        public event Action<State, State> StateChanged;

        public State Current => current;

        public State Previous => previous;

        //==================== LIFECYCLE =====================
        private void Update()
        {
            State next = PickState();
            if (next != current)
            {
                Transition(next);
            }

            if (current != null)
            {
                current.Tick();
            }
        }

        private void OnDisable()
        {
            if (current != null)
            {
                Transition(null);
            }
        }

        private void OnValidate()
        {
            if (states == null)
            {
                return;
            }

            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] == null)
                {
                    Debug.LogWarning($"{nameof(StateMachine)} on '{name}' has an empty slot at position {i} in its states list.", this);
                }
            }
        }

        //==================== PUBLIC API =====================
        /// <summary>
        /// Switches to the given state immediately. Pass null to clear the active state.
        /// The priority cascade runs again on the next frame, so a forced state that returns
        /// false from CanRun is replaced by a higher priority state right away. To keep a forced
        /// state active for a while, start a timer in OnEnter and return true from CanRun until
        /// the timer ends.
        /// </summary>
        public void ChangeTo(State next)
        {
            if (next == current)
            {
                return;
            }

            Transition(next);
        }

        //==================== PRIVATE =====================
        private State PickState()
        {
            for (int i = 0; i < states.Length; i++)
            {
                State candidate = states[i];
                if (candidate == null || !candidate.enabled)
                {
                    continue;
                }

                if (candidate.CanRun())
                {
                    return candidate;
                }
            }

            return null;
        }

        private void Transition(State next)
        {
            if (current != null)
            {
                current.OnExit();
            }

            previous = current;
            current = next;

            if (current != null)
            {
                current.OnEnter();
            }

            currentStateName = current != null ? current.GetType().Name : NoStateLabel;

            if (logTransitions)
            {
                string from = previous != null ? previous.GetType().Name : NoStateLabel;
                Debug.Log($"[{nameof(StateMachine)}] '{name}': {from} to {currentStateName}.", this);
            }

            StateChanged?.Invoke(previous, current);
            stateChangedEvent?.Invoke();
        }
    }
}
