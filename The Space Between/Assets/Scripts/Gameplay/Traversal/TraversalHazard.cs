using UnityEngine;
using UnityEngine.Events;

namespace TheSpaceBetween
{
    /// <summary>
    /// Sends the player back to the entry of a room when the player touches this object. Used for the bottom of a
    /// pit and for falling obstacles, so every failure in a traversal route has the same, predictable result:
    /// the room is reset (see RoomReset) and the player tries the section again.
    ///
    /// Needs a trigger collider on this object. A moving hazard also needs a kinematic Rigidbody, so Unity reports
    /// the contact while it moves.
    /// </summary>
    public class TraversalHazard : MonoBehaviour
    {
        //==================== HAZARD =====================
        [Header("Hazard")]
        [Tooltip("The room that is reset when the player touches this hazard. Its entry point is where the player tries again.")]
        [SerializeField] private RoomReset room;

        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked when the player touches this hazard, before the room is reset. Use it for a sound or a camera shake.")]
        [SerializeField] private UnityEvent hitEvent;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write each hit to the Console.")]
        [SerializeField] private bool logHits;

        [Tooltip("How many times the player has touched this hazard.")]
        [ReadOnly, SerializeField] private int hits;

        private int lastHitFrame = -1;

        /// <summary>The room reset on a hit. A spawner sets it on the obstacles it creates.</summary>
        public RoomReset Room
        {
            get => room;
            set => room = value;
        }

        public int Hits => hits;

        //==================== LIFECYCLE =====================
        private void OnTriggerEnter(Collider other)
        {
            PlaceholderPlayerController player = other.GetComponentInParent<PlaceholderPlayerController>();

            // The player has more than one collider, and each can report the same touch.
            if (player == null || Time.frameCount == lastHitFrame)
            {
                return;
            }

            lastHitFrame = Time.frameCount;
            hits++;
            hitEvent?.Invoke();

            if (logHits)
            {
                Debug.Log($"[{nameof(TraversalHazard)}] '{name}' hit the player. Resetting '{(room != null ? room.name : "no room")}'.", this);
            }

            if (room == null)
            {
                Debug.LogWarning($"[{nameof(TraversalHazard)}] '{name}' has no Room, so touching it does nothing. Assign one.", this);
                return;
            }

            room.ResetRoom();
        }
    }
}
