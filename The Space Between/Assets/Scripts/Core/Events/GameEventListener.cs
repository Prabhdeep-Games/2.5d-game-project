using UnityEngine;
using UnityEngine.Events;

namespace TheSpaceBetween
{
    /// <summary>
    /// Reacts to a GameEvent asset by invoking a UnityEvent, so a designer can wire responses
    /// (play a sound, open a door, show an effect) in the Inspector without writing code.
    /// Place it on any GameObject that should react.
    /// </summary>
    public class GameEventListener : MonoBehaviour
    {
        //==================== CONFIGURATION =====================
        [Header("Event")]
        [Tooltip("The event asset this listener reacts to.")]
        [SerializeField] private GameEvent gameEvent;

        [Header("Response")]
        [Tooltip("Invoked each time the event is raised while this component is enabled.")]
        [SerializeField] private UnityEvent response;

        //==================== LIFECYCLE =====================
        private void OnEnable()
        {
            if (gameEvent == null)
            {
                Debug.LogWarning($"{nameof(GameEventListener)} on '{name}' has no {nameof(GameEvent)} assigned and will do nothing.", this);
                return;
            }

            gameEvent.Raised += OnEventRaised;
        }

        private void OnDisable()
        {
            if (gameEvent != null)
            {
                gameEvent.Raised -= OnEventRaised;
            }
        }

        //==================== PRIVATE =====================
        private void OnEventRaised()
        {
            response?.Invoke();
        }
    }
}
