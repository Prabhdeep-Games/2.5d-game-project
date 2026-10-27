using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace TheSpaceBetween
{
    /// <summary>
    /// Lets this object swap positions with the Swappable it is aiming at.
    ///
    /// Choosing the target (see SwapAimMode):
    ///   Pointer  A thick ray is cast from this object along the aim direction (mouse cursor or
    ///            right stick). The first Swappable the ray reaches is the target. If something
    ///            solid in the Sight Blockers layers is reached first, there is no target.
    ///   Facing   The same ray, aimed left or right to match the way the player last moved.
    ///   Nearest  The old behaviour: the nearest valid Swappable in any direction.
    /// The aimed target is never replaced by an object behind it. If it is rejected, the swap is
    /// rejected and the reason is reported.
    ///
    /// Rules checked for the aimed target:
    ///   1. The target is within range.
    ///   2. The target is not locked.
    ///   3. Nothing in the Sight Blockers layers lies between the two. Leave glass layers out of
    ///      that mask and glass lets the player swap across it.
    ///   4. Both bodies fit at their new positions without overlapping solid geometry.
    /// Bodies trade standing points (the middle of the bottom of each body), so a tall player and
    /// a short crate both keep their feet at the other's floor level.
    ///
    /// Both bodies lose all momentum when they swap.
    /// </summary>
    [RequireComponent(typeof(Swappable))]
    public class SwapAbility : MonoBehaviour
    {
        private const int MaxCandidates = 32;
        private const float ClearanceSkin = 0.02f;
        private const float PointerMovedThreshold = 1f;

        //==================== INPUT =====================
        [Header("Input")]
        [Tooltip("Pressing this action attempts a swap.")]
        [SerializeField] private InputAction swapAction = new InputAction("Swap", InputActionType.Button);

        [Tooltip("Aim direction from a stick. Pointer mode only.")]
        [SerializeField] private InputAction aimStickAction = new InputAction("Aim Stick", InputActionType.Value);

        [Tooltip("Screen position of the mouse cursor. Pointer mode only.")]
        [SerializeField] private InputAction pointerAction = new InputAction("Pointer", InputActionType.Value);

        //==================== TARGETING =====================
        [Header("Targeting")]
        [Tooltip("How the target is chosen. Pointer: mouse or right stick. Facing: the way the player last moved. Nearest: old behaviour, nearest object in any direction.")]
        [SerializeField] private SwapAimMode aimMode = SwapAimMode.Pointer;

        [Tooltip("Thickness of the aim ray in world units. A thicker ray is more forgiving, a thin one needs a precise aim.")]
        [SerializeField, Min(0.05f)] private float aimRadius = 0.35f;

        [Tooltip("How far the stick must be pushed before it counts as aiming.")]
        [SerializeField, Range(0.1f, 0.9f)] private float stickDeadZone = 0.3f;

        [Tooltip("Camera used to turn the cursor into a world position. Empty means the main camera.")]
        [SerializeField] private Camera aimCamera;

        //==================== RULES =====================
        [Header("Rules")]
        [Tooltip("Maximum distance to a target, measured between the centers of the two bodies, in world units.")]
        [SerializeField, Min(0.1f)] private float range = 8f;

        [Tooltip("Layers that can hold a Swappable. Keep this narrow so the range check stays cheap.")]
        [SerializeField] private LayerMask swappableLayers = ~0;

        [Tooltip("Layers that block the view between the two bodies. Leave glass out of this mask so glass does not block a swap.")]
        [SerializeField] private LayerMask sightBlockers = ~0;

        [Tooltip("Layers that count as solid when checking whether a body fits at its new position.")]
        [SerializeField] private LayerMask clearanceLayers = ~0;

        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked after a successful swap.")]
        [SerializeField] private UnityEvent swappedEvent;

        [Tooltip("Invoked when a swap attempt is rejected.")]
        [SerializeField] private UnityEvent rejectedEvent;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write the result of every swap attempt to the Console.")]
        [SerializeField] private bool logAttempts;

        [Tooltip("Draw the range, the aim ray and the current target when selected.")]
        [SerializeField] private bool drawGizmos = true;

        [Tooltip("Result of the most recent attempt.")]
        [ReadOnly, SerializeField] private SwapRejection lastResult;

        [Tooltip("The object currently aimed at. Empty when nothing is aimed at.")]
        [ReadOnly, SerializeField] private Swappable currentTarget;

        [Tooltip("Whether the current target could be swapped with right now. None means yes.")]
        [ReadOnly, SerializeField] private SwapRejection currentStatus;

        protected Swappable self;
        private IFacingSource facingSource;
        private Vector3 aimDirection = Vector3.right;
        private Vector3 aimEnd;
        private Vector2 lastPointer;
        private bool stickWasLastUsed;

        private readonly Collider[] candidateBuffer = new Collider[MaxCandidates];
        private readonly Collider[] clearanceBuffer = new Collider[MaxCandidates];
        private readonly Swappable[] candidates = new Swappable[MaxCandidates];
        private readonly RaycastHit[] castBuffer = new RaycastHit[MaxCandidates];

        //==================== OUTPUTS =====================
        /// <summary>Raised after a successful swap. The argument is the object swapped with.</summary>
        public event Action<Swappable> Swapped;

        /// <summary>Raised when an attempt is rejected, with the reason.</summary>
        public event Action<SwapRejection> SwapRejected;

        public SwapRejection LastResult => lastResult;

        public float Range => range;

        /// <summary>The object currently aimed at, or null. Always null while a swap is playing.</summary>
        public Swappable Target => IsSwapping ? null : currentTarget;

        /// <summary>Whether the current target could be swapped with. None means the swap would work.</summary>
        public SwapRejection TargetStatus => currentStatus;

        /// <summary>Unit vector of the current aim in the world. Not used in Nearest mode.</summary>
        public Vector3 AimDirection => aimDirection;

        /// <summary>Where the aim ray ends: the target, the solid thing that stopped it, or the end of the range.</summary>
        public Vector3 AimEndPoint => aimEnd;

        /// <summary>The point the aim starts from, the middle of this body.</summary>
        public Vector3 Origin => self != null ? self.Center : transform.position;

        //==================== LIFECYCLE =====================
        private void Reset()
        {
            swapAction = new InputAction("Swap", InputActionType.Button, "<Keyboard>/e");
            swapAction.AddBinding("<Gamepad>/buttonWest");

            aimStickAction = new InputAction("Aim Stick", InputActionType.Value, "<Gamepad>/rightStick");
            pointerAction = new InputAction("Pointer", InputActionType.Value, "<Mouse>/position");
        }

        protected virtual void Awake()
        {
            self = GetComponent<Swappable>();
            facingSource = GetComponent<IFacingSource>();
        }

        protected virtual void OnEnable()
        {
            swapAction.performed += OnSwapPerformed;
            swapAction.Enable();
            aimStickAction.Enable();
            pointerAction.Enable();
        }

        protected virtual void OnDisable()
        {
            swapAction.performed -= OnSwapPerformed;
            swapAction.Disable();
            aimStickAction.Disable();
            pointerAction.Disable();
        }

        protected virtual void Update()
        {
            if (!IsSwapping)
            {
                RefreshTargeting();
            }
        }

        //==================== PUBLIC API =====================
        /// <summary>True while a swap is still playing out. New attempts are ignored until it ends.</summary>
        public virtual bool IsSwapping => false;

        /// <summary>Sets the aim direction by hand. Pointer mode overwrites it as soon as the mouse or stick moves.</summary>
        public void SetAimDirection(Vector3 direction)
        {
            direction.z = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                aimDirection = direction.normalized;
            }
        }

        /// <summary>Attempts a swap with the aimed target. Returns true when a swap was accepted.</summary>
        public bool TrySwap()
        {
            if (IsSwapping)
            {
                return false;
            }

            RefreshTargeting();

            if (currentTarget == null || currentStatus != SwapRejection.None)
            {
                return Finish(currentStatus, null);
            }

            Swappable chosen = currentTarget;
            Execute(chosen);
            return Finish(SwapRejection.None, chosen);
        }

        //==================== TARGETING =====================
        private void OnSwapPerformed(InputAction.CallbackContext context)
        {
            TrySwap();
        }

        // Works out the aim and the target from the current state of the world.
        private void RefreshTargeting()
        {
            if (self == null)
            {
                return;
            }

            if (aimMode == SwapAimMode.Nearest)
            {
                ResolveNearest();
                return;
            }

            UpdateAimDirection();
            ResolveAimed();
        }

        private void UpdateAimDirection()
        {
            if (aimMode == SwapAimMode.Facing)
            {
                SetAimDirection(facingSource != null ? facingSource.Facing : Vector3.right);
                return;
            }

            Vector2 stick = aimStickAction.ReadValue<Vector2>();
            if (stick.magnitude >= stickDeadZone)
            {
                SetAimDirection(new Vector3(stick.x, stick.y, 0f));
                stickWasLastUsed = true;
                return;
            }

            // With no stick input, the cursor sets the aim. If the player used the stick last and
            // the cursor has not moved, the stick's aim is kept.
            Vector2 pointer = pointerAction.ReadValue<Vector2>();
            bool pointerMoved = (pointer - lastPointer).sqrMagnitude > PointerMovedThreshold;
            lastPointer = pointer;

            if (stickWasLastUsed && !pointerMoved)
            {
                return;
            }

            Camera cam = aimCamera != null ? aimCamera : Camera.main;
            if (cam == null)
            {
                return;
            }

            stickWasLastUsed = false;
            Vector3 origin = self.Center;
            Plane gamePlane = new Plane(Vector3.forward, origin);
            Ray ray = cam.ScreenPointToRay(pointer);
            if (gamePlane.Raycast(ray, out float distance))
            {
                SetAimDirection(ray.GetPoint(distance) - origin);
            }
        }

        // Casts along the aim direction and picks the first Swappable the cast reaches.
        private void ResolveAimed()
        {
            Vector3 origin = self.Center;
            aimEnd = origin + aimDirection * range;
            currentTarget = null;
            currentStatus = SwapRejection.NoTargetInRange;

            LayerMask castMask = swappableLayers | sightBlockers;
            int hits = Physics.SphereCastNonAlloc(origin, aimRadius, aimDirection, castBuffer, range, castMask, QueryTriggerInteraction.Ignore);
            SortHitsByDistance(hits);

            for (int i = 0; i < hits; i++)
            {
                Collider hitCollider = castBuffer[i].collider;
                Swappable owner = hitCollider.GetComponentInParent<Swappable>();

                if (owner == self)
                {
                    continue;
                }

                if (owner != null)
                {
                    if (Vector3.Distance(origin, owner.Center) > range)
                    {
                        return;
                    }

                    currentTarget = owner;
                    currentStatus = Evaluate(owner);
                    aimEnd = owner.Center;
                    return;
                }

                // A solid thing that is not swappable ends the aim, unless it is something like glass.
                if (IsInMask(sightBlockers, hitCollider.gameObject.layer))
                {
                    currentStatus = SwapRejection.LineOfSightBlocked;
                    aimEnd = castBuffer[i].point;
                    return;
                }
            }
        }

        private void SortHitsByDistance(int count)
        {
            for (int i = 1; i < count; i++)
            {
                RaycastHit item = castBuffer[i];
                int j = i - 1;
                while (j >= 0 && castBuffer[j].distance > item.distance)
                {
                    castBuffer[j + 1] = castBuffer[j];
                    j--;
                }

                castBuffer[j + 1] = item;
            }
        }

        private static bool IsInMask(LayerMask mask, int layer)
        {
            return (mask.value & (1 << layer)) != 0;
        }

        // The old rule: the nearest valid target in any direction.
        private void ResolveNearest()
        {
            currentTarget = null;
            currentStatus = SwapRejection.NoTargetInRange;
            aimEnd = self.Center;

            int count = GatherCandidates();
            for (int i = 0; i < count; i++)
            {
                SwapRejection reason = Evaluate(candidates[i]);
                if (i == 0)
                {
                    currentTarget = candidates[i];
                    currentStatus = reason;
                    aimEnd = candidates[i].Center;
                }

                if (reason == SwapRejection.None)
                {
                    currentTarget = candidates[i];
                    currentStatus = reason;
                    aimEnd = candidates[i].Center;
                    return;
                }
            }
        }

        // Fills the candidates array, nearest first, and returns how many were found.
        private int GatherCandidates()
        {
            int hits = Physics.OverlapSphereNonAlloc(self.Center, range, candidateBuffer, swappableLayers, QueryTriggerInteraction.Ignore);

            int count = 0;
            for (int i = 0; i < hits; i++)
            {
                Swappable found = candidateBuffer[i].GetComponentInParent<Swappable>();
                if (found == null || found == self || IsListed(found, count))
                {
                    continue;
                }

                if (Vector3.Distance(self.Center, found.Center) > range)
                {
                    continue;
                }

                candidates[count] = found;
                count++;
            }

            // Insertion sort by distance, since the list is tiny.
            for (int i = 1; i < count; i++)
            {
                Swappable item = candidates[i];
                float distance = (item.Center - self.Center).sqrMagnitude;
                int j = i - 1;
                while (j >= 0 && (candidates[j].Center - self.Center).sqrMagnitude > distance)
                {
                    candidates[j + 1] = candidates[j];
                    j--;
                }

                candidates[j + 1] = item;
            }

            return count;
        }

        private bool IsListed(Swappable item, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (candidates[i] == item)
                {
                    return true;
                }
            }

            return false;
        }

        //==================== RULES =====================
        private SwapRejection Evaluate(Swappable target)
        {
            if (!target.CanSwap || !self.CanSwap)
            {
                return SwapRejection.TargetLocked;
            }

            if (!HasLineOfSight(target))
            {
                return SwapRejection.LineOfSightBlocked;
            }

            if (!HasClearance(target))
            {
                return SwapRejection.DestinationBlocked;
            }

            return SwapRejection.None;
        }

        private bool HasLineOfSight(Swappable target)
        {
            Vector3 from = self.Center;
            Vector3 to = target.Center;

            if (!Physics.Linecast(from, to, out RaycastHit hit, sightBlockers, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            // Hitting the target itself or this object's own collider does not count as blocked.
            Swappable hitOwner = hit.collider.GetComponentInParent<Swappable>();
            return hitOwner == target || hitOwner == self;
        }

        private bool HasClearance(Swappable target)
        {
            Vector3 shiftOfSelf = SelfShiftTo(target);

            // Where each body will be after the swap, shrunk a little so resting on a floor is not an overlap.
            Bounds selfAfter = ShiftedAndShrunk(self.WorldBounds, shiftOfSelf);
            Bounds targetAfter = ShiftedAndShrunk(target.WorldBounds, -shiftOfSelf);

            if (selfAfter.Intersects(targetAfter))
            {
                return false;
            }

            return FitsAt(selfAfter, self, target) && FitsAt(targetAfter, self, target);
        }

        private bool FitsAt(Bounds area, Swappable first, Swappable second)
        {
            int hits = Physics.OverlapBoxNonAlloc(area.center, area.extents, clearanceBuffer, Quaternion.identity, clearanceLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits; i++)
            {
                Swappable owner = clearanceBuffer[i].GetComponentInParent<Swappable>();
                if (owner != first && owner != second)
                {
                    return false;
                }
            }

            return true;
        }

        private static Bounds ShiftedAndShrunk(Bounds source, Vector3 shift)
        {
            Vector3 extents = Vector3.Max(source.extents - Vector3.one * ClearanceSkin, Vector3.zero);
            return new Bounds(source.center + shift, extents * 2f);
        }

        /// <summary>
        /// Carries out an accepted swap. The default moves both bodies at once and removes their
        /// momentum. Subclasses override this to play a different kind of swap.
        /// </summary>
        protected virtual void Execute(Swappable target)
        {
            Vector3 shiftOfSelf = SelfShiftTo(target);

            self.MoveTo(self.Position + shiftOfSelf);
            target.MoveTo(target.Position - shiftOfSelf);
            Physics.SyncTransforms();
        }

        /// <summary>
        /// How far this body must move to trade standing points with the target. The target moves
        /// by the opposite amount.
        /// </summary>
        protected Vector3 SelfShiftTo(Swappable target)
        {
            return target.Anchor - self.Anchor;
        }

        /// <summary>Whether something solid is directly below the bottom of the given body.</summary>
        protected bool IsStandingOnSomething(Swappable body)
        {
            const float probeLift = 0.05f;
            const float probeLength = 0.15f;

            Vector3 origin = body.Anchor + Vector3.up * probeLift;
            return Physics.Raycast(origin, Vector3.down, probeLength, clearanceLayers, QueryTriggerInteraction.Ignore);
        }

        private bool Finish(SwapRejection result, Swappable target)
        {
            lastResult = result;

            if (logAttempts)
            {
                string detail = result == SwapRejection.None && target != null ? $" with '{target.name}'" : string.Empty;
                Debug.Log($"[{nameof(SwapAbility)}] '{name}': {SwapRejectionText.Describe(result)}{detail}", this);
            }

            if (result == SwapRejection.None)
            {
                Swapped?.Invoke(target);
                swappedEvent?.Invoke();
                return true;
            }

            SwapRejected?.Invoke(result);
            rejectedEvent?.Invoke();
            return false;
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
            {
                return;
            }

            Vector3 center = transform.position;
            Swappable own = GetComponent<Swappable>();
            if (own != null)
            {
                center = own.Center;
            }

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(center, range);

            if (!Application.isPlaying)
            {
                return;
            }

            // The aim ray: green when the target is valid, red when rejected, grey with no target.
            Gizmos.color = currentTarget == null ? Color.gray : currentStatus == SwapRejection.None ? Color.green : Color.red;
            Gizmos.DrawLine(center, aimEnd);
            Gizmos.DrawWireSphere(aimEnd, aimRadius);
        }
    }
}
