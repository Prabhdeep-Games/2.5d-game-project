using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace TheSpaceBetween
{
    /// <summary>
    /// Lets this object swap positions with the nearest valid Swappable in range.
    ///
    /// Rules, checked in this order for each candidate (nearest first):
    ///   1. The target is within range.
    ///   2. The target is not locked.
    ///   3. Nothing in the Sight Blockers layers lies between the two. Leave glass layers out of
    ///      that mask and glass lets the player swap across it.
    ///   4. Both bodies fit at their new positions without overlapping solid geometry.
    /// Bodies trade standing points (the middle of the bottom of each body), so a tall player and
    /// a short crate both keep their feet at the other's floor level.
    /// The first candidate that passes everything is swapped. If none passes, nothing changes and
    /// the reason of the nearest candidate is reported.
    ///
    /// Both bodies lose all momentum when they swap.
    /// </summary>
    [RequireComponent(typeof(Swappable))]
    public class SwapAbility : MonoBehaviour
    {
        private const int MaxCandidates = 32;
        private const float ClearanceSkin = 0.02f;

        //==================== INPUT =====================
        [Header("Input")]
        [Tooltip("Pressing this action attempts a swap.")]
        [SerializeField] private InputAction swapAction = new InputAction("Swap", InputActionType.Button);

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

        [Tooltip("Draw the range and a line to the nearest candidate when selected.")]
        [SerializeField] private bool drawGizmos = true;

        [Tooltip("Result of the most recent attempt.")]
        [ReadOnly, SerializeField] private SwapRejection lastResult;

        protected Swappable self;
        private readonly Collider[] candidateBuffer = new Collider[MaxCandidates];
        private readonly Collider[] clearanceBuffer = new Collider[MaxCandidates];
        private readonly Swappable[] candidates = new Swappable[MaxCandidates];

        //==================== OUTPUTS =====================
        /// <summary>Raised after a successful swap. The argument is the object swapped with.</summary>
        public event Action<Swappable> Swapped;

        /// <summary>Raised when an attempt is rejected, with the reason.</summary>
        public event Action<SwapRejection> SwapRejected;

        public SwapRejection LastResult => lastResult;

        public float Range => range;

        //==================== LIFECYCLE =====================
        private void Reset()
        {
            swapAction = new InputAction("Swap", InputActionType.Button, "<Keyboard>/e");
            swapAction.AddBinding("<Gamepad>/buttonWest");
        }

        protected virtual void Awake()
        {
            self = GetComponent<Swappable>();
        }

        protected virtual void OnEnable()
        {
            swapAction.performed += OnSwapPerformed;
            swapAction.Enable();
        }

        protected virtual void OnDisable()
        {
            swapAction.performed -= OnSwapPerformed;
            swapAction.Disable();
        }

        //==================== PUBLIC API =====================
        /// <summary>True while a swap is still playing out. New attempts are ignored until it ends.</summary>
        public virtual bool IsSwapping => false;

        /// <summary>Attempts a swap with the nearest valid target. Returns true when a swap was accepted.</summary>
        public bool TrySwap()
        {
            if (IsSwapping)
            {
                return false;
            }

            int count = GatherCandidates();
            if (count == 0)
            {
                return Finish(SwapRejection.NoTargetInRange, null);
            }

            SwapRejection nearestReason = SwapRejection.None;
            for (int i = 0; i < count; i++)
            {
                SwapRejection reason = Evaluate(candidates[i]);
                if (reason == SwapRejection.None)
                {
                    Execute(candidates[i]);
                    return Finish(SwapRejection.None, candidates[i]);
                }

                if (i == 0)
                {
                    nearestReason = reason;
                }
            }

            return Finish(nearestReason, null);
        }

        //==================== PRIVATE =====================
        private void OnSwapPerformed(InputAction.CallbackContext context)
        {
            TrySwap();
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
        }
    }
}
