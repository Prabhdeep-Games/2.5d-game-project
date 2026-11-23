using UnityEngine;
using UnityEngine.Events;

namespace TheSpaceBetween
{
    /// <summary>
    /// A sealed door that slides open once a control panel powers it or a script calls Unlock.
    /// A security camera lockdown keeps it closed, and it never closes onto the player or a crate.
    ///
    /// With an Unlock Animator (the Security Door), opening has two steps: the lock animation plays first, then the
    /// door slides. Closing runs the other way round: the door slides shut and blocks straight away, then the lock
    /// animation plays backwards. The door poses the animation itself from one number, so a door that is told to open
    /// while it is still relocking simply turns round, with no jump.
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

        //==================== UNLOCK ANIMATION =====================
        [Header("Unlock Animation")]
        [Tooltip("Optional. Plays the state below before the door moves, and backwards after it has closed. Empty: the door only slides.")]
        [SerializeField] private Animator unlockAnimator;

        [Tooltip("The Animator state that holds the unlock animation, played from its start (locked) to its end (unlocked).")]
        [SerializeField] private string unlockState = "Open";

        [Tooltip("Playback speed of the unlock animation. 1 is the speed it was animated at; above 1 is faster.")]
        [SerializeField, Min(0.1f)] private float animationSpeed = 1.5f;

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

        [Tooltip("How far the unlock animation has played: 0 locked, 1 unlocked.")]
        [ReadOnly, SerializeField, Range(0f, 1f)] private float unlockProgress;

        private Vector3 closedLocalPosition;
        private Vector3 openLocalPosition;
        private float speed;
        private Collider doorCollider;
        private int unlockStateHash;
        private float unlockSeconds;

        //==================== PUBLIC =====================
        /// <summary>Locked means unpowered. A door in lockdown is not locked, but still refuses to open.</summary>
        public bool IsLocked => !IsPowered;

        public DoorState State => state;

        /// <summary>True while the door waits for something to leave the doorway before closing.</summary>
        public bool IsObstructed => isObstructed;

        /// <summary>How far the unlock animation has played: 0 locked, 1 unlocked. Always 1 without an animation.</summary>
        public float UnlockProgress => HasUnlockAnimation ? unlockProgress : 1f;

        /// <summary>Seconds the unlock animation takes at its playback speed. 0 without an animation.</summary>
        public float UnlockSeconds => unlockSeconds;

        private bool HasUnlockAnimation => unlockSeconds > 0f;

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
            PrepareUnlockAnimation();

            bool open = startsOpen && !startsLocked;
            if (open)
            {
                transform.localPosition = openLocalPosition;
                state = DoorState.Open;
            }

            unlockProgress = open ? 1f : 0f;
            PoseUnlockAnimation();
        }

        private void Update()
        {
            isObstructed = false;
            switch (state)
            {
                case DoorState.Opening:
                    if (StepUnlockAnimation(unlocking: true))
                    {
                        Slide(opening: true);
                    }

                    break;

                case DoorState.Closing:
                    Slide(opening: false);
                    break;

                case DoorState.Closed:
                    // Shut and blocking already; the lock turns back as the finishing touch.
                    StepUnlockAnimation(unlocking: false);
                    break;
            }
        }

        //==================== UNLOCK ANIMATION =====================
        // The Animator never plays on its own: speed 0, posed by the door, so door and animation cannot drift apart.
        private void PrepareUnlockAnimation()
        {
            unlockSeconds = 0f;
            if (unlockAnimator == null || unlockAnimator.runtimeAnimatorController == null)
            {
                return;
            }

            float clipSeconds = 0f;
            foreach (AnimationClip clip in unlockAnimator.runtimeAnimatorController.animationClips)
            {
                clipSeconds = Mathf.Max(clipSeconds, clip.length);
            }

            unlockStateHash = Animator.StringToHash(unlockState);
            unlockSeconds = clipSeconds / animationSpeed;
            unlockAnimator.speed = 0f;
        }

        // Moves the animation one frame toward unlocked or locked. True once it is there, or when there is no animation.
        private bool StepUnlockAnimation(bool unlocking)
        {
            if (!HasUnlockAnimation)
            {
                return true;
            }

            float target = unlocking ? 1f : 0f;
            if (unlockProgress == target)
            {
                return true;
            }

            unlockProgress = Mathf.MoveTowards(unlockProgress, target, Time.deltaTime / unlockSeconds);
            PoseUnlockAnimation();
            return unlockProgress == target;
        }

        private void PoseUnlockAnimation()
        {
            if (HasUnlockAnimation)
            {
                unlockAnimator.Play(unlockStateHash, 0, unlockProgress);
            }
        }

        //==================== SLIDING =====================
        private void Slide(bool opening)
        {
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
            if (!GeneratedRoom.ShowsGizmos(this))
            {
                return;
            }

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
