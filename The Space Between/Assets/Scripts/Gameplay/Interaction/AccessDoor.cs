using UnityEngine;
using UnityEngine.Events;

namespace TheSpaceBetween
{
    /// <summary>
    /// A sealed door that controls access. While locked it refuses to open, and it tells the world
    /// that it refused. Once unlocked it slides to its open position, which physically clears
    /// the way because the door keeps its collider.
    ///
    /// The door does not decide who may unlock it. A control panel powers it (powered means unlocked),
    /// a puzzle raises the GameEvent assigned as the unlock event, or a script calls Unlock directly.
    /// A security camera can put it in lockdown: it then closes and refuses to open, even when unlocked,
    /// until the camera releases it. If Opens When Unlocked is on, it opens again by itself.
    ///
    /// The door never closes onto the player or a crate: it waits until the way is clear.
    /// </summary>
    public class AccessDoor : Mechanism
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
        [Tooltip("Whether the door starts locked. A locked door refuses to open until Unlock is called or a panel powers it.")]
        [SerializeField] private bool startsLocked = true;

        [Tooltip("Optional event that unlocks this door when raised, for example when the player picks up a key.")]
        [SerializeField] private GameEvent unlockEvent;

        [Tooltip("Open the door as soon as it is unlocked, and again when a lockdown ends.")]
        [SerializeField] private bool opensWhenUnlocked = true;

        [Tooltip("Start in the open position. Only used when the door starts unlocked, for example a door a security camera seals.")]
        [SerializeField] private bool startsOpen;

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

        [Tooltip("Invoked when something tried to open the door while it was locked or locked down. Use it for a denied sound or light.")]
        [SerializeField] private UnityEvent openRejectedEvent;

        //==================== DEBUG =====================
        [Header("Door State")]
        [Tooltip("Current movement state of the door.")]
        [ReadOnly, SerializeField] private DoorState state = DoorState.Closed;

        [Tooltip("Whether the door is waiting because something is standing where it would close.")]
        [ReadOnly, SerializeField] private bool isObstructed;

        private Vector3 closedLocalPosition;
        private Vector3 openLocalPosition;
        private float speed;
        private Collider doorCollider;

        //==================== PUBLIC API =====================
        /// <summary>Locked means unpowered. A door in lockdown is not locked, but still refuses to open.</summary>
        public bool IsLocked => !IsPowered;

        public DoorState State => state;

        /// <summary>True while the door waits for something to leave the doorway before closing.</summary>
        public bool IsObstructed => isObstructed;

        /// <summary>Unlocks the door, and opens it when "Opens When Unlocked" is on.</summary>
        public void Unlock()
        {
            SetPowered(true);
        }

        /// <summary>Closes the door and locks it.</summary>
        public void Lock()
        {
            Close();
            SetPowered(false);
        }

        /// <summary>Starts opening. A locked or locked down door refuses and raises the rejected event.</summary>
        public void Open()
        {
            if (!IsActive)
            {
                Log(IsLockedDown ? "refused to open because it is in lockdown" : "refused to open because it is locked");
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

        //==================== MECHANISM =====================
        protected override bool StartsPowered => !startsLocked;

        protected override void OnActiveChanged(bool active)
        {
            if (!active)
            {
                Close();
                return;
            }

            if (opensWhenUnlocked)
            {
                Open();
            }
            else
            {
                Log("unlocked");
            }
        }

        //==================== LIFECYCLE =====================
        protected override void Awake()
        {
            base.Awake();
            closedLocalPosition = transform.localPosition;
            openLocalPosition = closedLocalPosition + openOffset;
            speed = openOffset.magnitude / travelTime;
            doorCollider = GetComponent<Collider>();

            if (startsOpen && !startsLocked)
            {
                transform.localPosition = openLocalPosition;
                state = DoorState.Open;
            }
        }

        private void OnEnable()
        {
            if (unlockEvent != null)
            {
                unlockEvent.Raised += Unlock;
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (unlockEvent != null)
            {
                unlockEvent.Raised -= Unlock;
            }
        }

        private void Update()
        {
            isObstructed = false;
            if (state != DoorState.Opening && state != DoorState.Closing)
            {
                return;
            }

            bool opening = state == DoorState.Opening;
            Vector3 target = opening ? openLocalPosition : closedLocalPosition;
            Vector3 next = Vector3.MoveTowards(transform.localPosition, target, speed * Time.deltaTime);

            // A closing door waits rather than pushing into whoever stands in the doorway.
            if (!opening)
            {
                Vector3 worldStep = transform.parent != null
                    ? transform.parent.TransformVector(next - transform.localPosition)
                    : next - transform.localPosition;
                if (KinematicSafety.FindMovableInPath(doorCollider, worldStep) != null)
                {
                    isObstructed = true;
                    return;
                }
            }

            transform.localPosition = next;

            // The doorway check reads the collider's bounds, which only follow the transform once physics is told.
            if (doorCollider != null)
            {
                Physics.SyncTransforms();
            }

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
