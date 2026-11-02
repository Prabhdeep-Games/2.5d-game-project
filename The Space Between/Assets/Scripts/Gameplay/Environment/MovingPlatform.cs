using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// A platform moved by machinery. It travels between its starting position and Start + End Offset, and
    /// carries whatever stands on it: the player, crates and other swappable objects.
    ///
    /// Modes:
    ///   Go To End While Active: travels to the end while active, and back to the start when not.
    ///   Loop While Active:      shuttles between both ends while active, and stops where it is when not.
    ///
    /// It never crushes anything. It waits while a body is in its way, or while a rider would be pushed into
    /// a ceiling, and continues when the way is clear.
    ///
    /// It runs before the player's movement each frame, so a rider moves with it and then walks on it.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(BoxCollider))]
    public class MovingPlatform : Mechanism
    {
        /// <summary>How the platform moves while it is active.</summary>
        public enum PlatformMode
        {
            GoToEndWhileActive,
            LoopWhileActive
        }

        // Height of the zone above the top face where riders are looked for. Covers the Character Controller skin.
        private const float RiderProbeHeight = 0.2f;
        private const float RiderProbeInset = 0.05f;
        private const int RiderBufferSize = 16;

        //==================== POWER =====================
        [Header("Power")]
        [Tooltip("Whether the platform starts powered. A control panel can change it.")]
        [SerializeField] private bool startsPowered;

        //==================== ROUTE =====================
        [Header("Route")]
        [Tooltip("What the platform does while active.")]
        [SerializeField] private PlatformMode mode = PlatformMode.GoToEndWhileActive;

        [Tooltip("Where the route ends, relative to the starting position, in world units.")]
        [SerializeField] private Vector3 endOffset = new Vector3(0f, 4f, 0f);

        [Tooltip("Travel speed in units per second.")]
        [SerializeField, Min(0.01f)] private float speed = 2f;

        [Tooltip("Seconds the platform waits at each end in Loop mode, so riders can step on and off.")]
        [SerializeField, Min(0f)] private float pauseAtEnds = 1f;

        //==================== RIDERS =====================
        [Header("Riders")]
        [Tooltip("Move the player and objects standing on the platform along with it.")]
        [SerializeField] private bool carryRiders = true;

        //==================== DEBUG =====================
        [Header("Platform State")]
        [Tooltip("Position along the route: 0 is the start, 1 is the end.")]
        [ReadOnly, SerializeField] private float progress;

        [Tooltip("Whether the platform is waiting because something is in its way or a rider would be crushed.")]
        [ReadOnly, SerializeField] private bool isObstructed;

        [Tooltip("How many bodies are standing on the platform.")]
        [ReadOnly, SerializeField] private int riderCount;

        private readonly Collider[] riderBuffer = new Collider[RiderBufferSize];
        private readonly List<Transform> riders = new List<Transform>();
        private BoxCollider box;
        private Vector3 startPosition;
        private float loopDirection = 1f;
        private float pauseUntil;

        //==================== OUTPUTS =====================
        /// <summary>Position along the route: 0 is the start, 1 is the end.</summary>
        public float Progress => progress;

        public bool IsObstructed => isObstructed;

        public int RiderCount => riderCount;

        public Vector3 StartPosition => startPosition;

        public Vector3 EndPosition => startPosition + endOffset;

        //==================== MECHANISM =====================
        protected override bool StartsPowered => startsPowered;

        protected override void OnActiveChanged(bool active)
        {
            Log(active ? "started moving" : "stopped");
        }

        //==================== LIFECYCLE =====================
        protected override void Awake()
        {
            base.Awake();
            box = GetComponent<BoxCollider>();
            startPosition = transform.position;
        }

        private void Update()
        {
            isObstructed = false;
            float target = TargetProgress();
            if (Mathf.Approximately(progress, target) || Time.time < pauseUntil)
            {
                return;
            }

            float length = endOffset.magnitude;
            float next = length > 0f ? Mathf.MoveTowards(progress, target, speed * Time.deltaTime / length) : target;
            Vector3 delta = endOffset * (next - progress);

            FindRiders();
            if (IsBlocked(delta))
            {
                isObstructed = true;
                return;
            }

            // Moving up, riders go first so the platform never rises into them. Moving down, the platform goes
            // first (and physics is told at once) so it does not hold them up.
            if (delta.y > 0f)
            {
                MoveRiders(delta);
                transform.position += delta;
            }
            else
            {
                transform.position += delta;
                Physics.SyncTransforms();
                MoveRiders(delta);
            }

            Physics.SyncTransforms();
            progress = next;

            if (mode == PlatformMode.LoopWhileActive && Mathf.Approximately(progress, target))
            {
                loopDirection = -loopDirection;
                pauseUntil = Time.time + pauseAtEnds;
            }
        }

        //==================== PRIVATE =====================
        private float TargetProgress()
        {
            if (mode == PlatformMode.GoToEndWhileActive)
            {
                return IsActive ? 1f : 0f;
            }

            if (!IsActive)
            {
                return progress;
            }

            return loopDirection > 0f ? 1f : 0f;
        }

        // Everything movable whose collider reaches into a thin zone just above the top face.
        private void FindRiders()
        {
            riders.Clear();
            riderCount = 0;
            if (!carryRiders)
            {
                return;
            }

            Bounds bounds = box.bounds;
            Vector3 center = new Vector3(bounds.center.x, bounds.max.y + RiderProbeHeight * 0.5f, bounds.center.z);
            Vector3 halfExtents = new Vector3(bounds.extents.x - RiderProbeInset, RiderProbeHeight * 0.5f, bounds.extents.z - RiderProbeInset);
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, riderBuffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Transform owner = KinematicSafety.MovableOwner(riderBuffer[i]);
                if (owner == null || owner == transform || riders.Contains(owner))
                {
                    continue;
                }

                // A body in the middle of a swap animation is held by the swap, not by the platform.
                Swappable swappable = owner.GetComponent<Swappable>();
                if (swappable != null && swappable.IsFrozen)
                {
                    continue;
                }

                riders.Add(owner);
            }

            riderCount = riders.Count;
        }

        private bool IsBlocked(Vector3 delta)
        {
            if (KinematicSafety.FindMovableInPath(box, delta, riders) != null)
            {
                return true;
            }

            for (int i = 0; i < riders.Count; i++)
            {
                if (KinematicSafety.WouldBeCrushed(riders[i], delta, box, riders))
                {
                    return true;
                }
            }

            return false;
        }

        private void MoveRiders(Vector3 delta)
        {
            for (int i = 0; i < riders.Count; i++)
            {
                Transform rider = riders[i];
                CharacterController controller = rider.GetComponent<CharacterController>();
                if (controller != null && controller.enabled)
                {
                    controller.Move(delta);
                    continue;
                }

                Rigidbody body = rider.GetComponent<Rigidbody>();
                if (body != null && !body.isKinematic)
                {
                    body.position += delta;
                }

                rider.position += delta;
            }
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            BoxCollider gizmoBox = GetComponent<BoxCollider>();
            if (gizmoBox == null)
            {
                return;
            }

            Vector3 start = Application.isPlaying ? startPosition : transform.position;
            Vector3 size = Vector3.Scale(gizmoBox.size, transform.lossyScale);
            Vector3 centerOffset = gizmoBox.bounds.center - transform.position;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(start, start + endOffset);
            Gizmos.DrawWireCube(start + endOffset + centerOffset, size);
        }
    }
}
