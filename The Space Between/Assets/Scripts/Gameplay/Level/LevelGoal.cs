using UnityEngine;
using UnityEngine.Events;

namespace TheSpaceBetween
{
    /// <summary>
    /// The end of a level. When the player's middle enters the Box Collider on this object, the level counts
    /// as complete: the assigned object (for example a "Level complete" sign) is shown and the event fires once.
    /// The collider should be a trigger on the Ignore Raycast layer, so nothing collides with it or aims at it.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class LevelGoal : MonoBehaviour
    {
        //==================== GOAL =====================
        [Header("Goal")]
        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private Transform player;

        [Tooltip("Shown when the goal is reached, hidden until then. Optional.")]
        [SerializeField] private GameObject showWhenReached;

        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked once, when the player first reaches the goal.")]
        [SerializeField] private UnityEvent reachedEvent;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write to the Console when the goal is reached.")]
        [SerializeField] private bool logReached = true;

        [Tooltip("Whether the player has reached the goal.")]
        [ReadOnly, SerializeField] private bool reached;

        [Tooltip("Seconds of play from the start of the scene until the goal was reached.")]
        [ReadOnly, SerializeField] private float reachedAfterSeconds;

        private BoxCollider area;

        public bool Reached => reached;

        //==================== LIFECYCLE =====================
        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        private void Awake()
        {
            area = GetComponent<BoxCollider>();

            if (player == null)
            {
                PlaceholderPlayerController controller = FindAnyObjectByType<PlaceholderPlayerController>();
                player = controller != null ? controller.transform : null;
            }

            if (showWhenReached != null)
            {
                showWhenReached.SetActive(false);
            }
        }

        private void Update()
        {
            if (reached || player == null || !area.bounds.Contains(player.position))
            {
                return;
            }

            reached = true;
            reachedAfterSeconds = Time.timeSinceLevelLoad;

            if (showWhenReached != null)
            {
                showWhenReached.SetActive(true);
            }

            reachedEvent?.Invoke();

            if (logReached)
            {
                Debug.Log($"[{nameof(LevelGoal)}] '{name}' reached after {reachedAfterSeconds:F1} s.", this);
            }
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmos()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null)
            {
                return;
            }

            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
