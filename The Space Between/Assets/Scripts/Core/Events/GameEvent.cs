using System;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// A named broadcast channel stored as an asset. Any script can raise it and any number of
    /// listeners can react, so the sender and the receivers never need to reference each other.
    ///
    /// Typical use: a swap succeeds and raises one event, and audio, visual effects, and
    /// doors each listen to that same event without knowing about the swap code.
    ///
    /// Subscribe from code with Raised (add in OnEnable, remove in OnDisable), or place a
    /// GameEventListener component to wire the response in the Inspector.
    /// </summary>
    [CreateAssetMenu(fileName = "NewGameEvent", menuName = "The Space Between/Events/Game Event")]
    public class GameEvent : ScriptableObject
    {
        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write a message to the Console every time this event is raised.")]
        [SerializeField] private bool logWhenRaised;

        [Tooltip("How many times this event was raised during the current play session.")]
        [ReadOnly, SerializeField] private int raiseCount;

        //==================== OUTPUTS =====================
        /// <summary>Invoked every time Raise is called.</summary>
        public event Action Raised;

        /// <summary>Number of times this event was raised during the current play session.</summary>
        public int RaiseCount => raiseCount;

        //==================== PUBLIC API =====================
        /// <summary>Notifies every subscriber that this event happened.</summary>
        public void Raise()
        {
            raiseCount++;

            if (logWhenRaised)
            {
                Debug.Log($"[GameEvent] '{name}' raised (count {raiseCount}).", this);
            }

            Raised?.Invoke();
        }

        //==================== LIFECYCLE =====================
        private void OnEnable()
        {
            // Session state must not survive between play sessions. Without this, subscribers
            // from a previous session would still be called when domain reload is disabled.
            raiseCount = 0;
            Raised = null;
        }
    }
}