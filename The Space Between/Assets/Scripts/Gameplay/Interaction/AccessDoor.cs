using UnityEngine;
using UnityEngine.Events;

namespace TheSpaceBetween
{
    /// <summary>
    /// A door that controls access. While locked it refuses to open, and it tells the world
    /// that it refused. Once unlocked it slides to its open position, which physically clears
    /// the way because the door keeps its collider.
    ///
    /// The door does not decide who may unlock it. A key pickup, a switch, or a puzzle raises
    /// the GameEvent assigned as the unlock event, or calls Unlock directly.
    /// </summary>
    public class AccessDoor : MonoBehaviour
    {
        /// <summary>Where the door is in its movement between closed and open.</summary>
        public enum DoorState
        {
            Closed,
            Opening,
            Open,
            Closing
        }

        private const float ArrivalDistanceSqr = 0.000001f;

        //==================== ACCESS =====================
        [Header("Access")]
        [Tooltip("Whether the door starts locked. A locked door refuses to open until Unlock is called.")]
        [SerializeField] private bool startsLocked = true;

        [Tooltip("Optional event that unlocks this door when raised, for example when the player picks up a key.")]
        [SerializeField] private GameEvent unlockEvent;

        [Tooltip("Open the door as soon as it is unlocked.")]
        [SerializeField] private bool opensWhenUnlocked = true;

        //==================== MOVEMENT =====================
        [Header("Movement")]
        [Tooltip("How far the door moves when open, in the local space of its parent. The default slides the door up by 3 units.")]
        [SerializeField] private Vector3 openOffset = new Vector3(0f, 3f, 0f);

        [Tooltip("Seconds the door needs to travel between closed and open.")]
        [SerializeField, Min(0.01f)] private float travelTime = 1f;

        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked when the door finishes opening.")]
        [SerializeField] private UnityEvent openedEvent;

        [Tooltip("Invoked when the door finishes closing.")]
        [SerializeField] private UnityEvent closedEvent;

        [Tooltip("Invoked when something tried to open the door while it was locked. Use it for a denied sound or light.")]
        [SerializeField] private UnityEvent openRejectedEvent;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write door messages to the Console, including why an open request was refused.")]
        [SerializeField] private bool logChanges;

        [Tooltip("Whether the door is locked right now.")]
        [ReadOnly, SerializeField] private bool isLocked;

        [Tooltip("Current movement state of the door.")]
        [ReadOnly, SerializeField] private DoorState state = DoorState.Closed;

        private Vector3 closedLocalPosition;
        private Vector3 openLocalPosition;
        private float speed;

        //==================== PUBLIC API =====================
        public bool IsLocked => isLocked;

        public DoorState State => state;

        /// <summary>Unlocks the door, and opens it when "Opens When Unlocked" is on.</summary>
        public void Unlock()
        {
            if (!isLocked)
            {
                return;
            }

            isLocked = false;
            Log("unlocked");

            if (opensWhenUnlocked)
            {
                Open();
            }
        }

        /// <summary>Closes the door and locks it.</summary>
        public void Lock()
        {
            Close();
            isLocked = true;
            Log("locked");
        }

        /// <summary>Starts opening. A locked door refuses and raises the rejected event.</summary>
        public void Open()
        {
            if (isLocked)
            {
                Log("refused to open because it is locked");
                openRejectedEvent?.Invoke();
                return;
            }

            if (state == DoorState.Open || state == DoorState.Opening)
            {
                return;
            }

            state = DoorState.Opening;
            Log("opening");
        }

        /// <summary>Starts closing. Works whether or not the door is locked.</summary>
        public void Close()
        {
            if (state == DoorState.Closed || state == DoorState.Closing)
            {
                return;
            }

            state = DoorState.Closing;
            Log("closing");
        }

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            closedLocalPosition = transform.localPosition;
            openLocalPosition = closedLocalPosition + openOffset;
            speed = openOffset.magnitude / travelTime;
            isLocked = startsLocked;
        }

        private void OnEnable()
        {
            if (unlockEvent != null)
            {
                unlockEvent.Raised += Unlock;
            }
        }

        private void OnDisable()
        {
            if (unlockEvent != null)
            {
                unlockEvent.Raised -= Unlock;
            }
        }

        private void Update()
        {
            if (state != DoorState.Opening && state != DoorState.Closing)
            {
                return;
            }

            bool opening = state == DoorState.Opening;
            Vector3 target = opening ? openLocalPosition : closedLocalPosition;

            transform.localPosition = Vector3.MoveTowards(transform.localPosition, target, speed * Time.deltaTime);

            // A door with no offset has no distance to travel, so it finishes at once.
            bool arrived = speed <= 0f || (transform.localPosition - target).sqrMagnitude <= ArrivalDistanceSqr;
            if (!arrived)
            {
                return;
            }

            transform.localPosition = target;
            state = opening ? DoorState.Open : DoorState.Closed;
            Log(opening ? "open" : "closed");

            if (opening)
            {
                openedEvent?.Invoke();
            }
            else
            {
                closedEvent?.Invoke();
            }
        }

        //==================== PRIVATE =====================
        private void Log(string message)
        {
            if (logChanges)
            {
                Debug.Log($"[{nameof(AccessDoor)}] '{name}' {message}.", this);
            }
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            Vector3 closedLocal = Application.isPlaying ? closedLocalPosition : transform.localPosition;
            Vector3 openLocal = closedLocal + openOffset;

            Transform parent = transform.parent;
            Vector3 closedWorld = parent != null ? parent.TransformPoint(closedLocal) : closedLocal;
            Vector3 openWorld = parent != null ? parent.TransformPoint(openLocal) : openLocal;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(closedWorld, openWorld);
            Gizmos.DrawWireSphere(openWorld, 0.15f);
        }
    }
}
