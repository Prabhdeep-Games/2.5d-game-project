using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace TheSpaceBetween
{
    //================================================================================================================
    // SWAP ABILITY
    // Everything that decides and performs a swap: input, aiming, the rules, the gesture and the motion. Everything
    // the player sees and hears about a swap lives in SwapFeedback. The object side of a swap is Swappable.
    //================================================================================================================

    /// <summary>
    /// Lets this object swap positions with the Swappable it is aiming at.
    ///
    /// <para><b>Targeting</b> (see <see cref="SwapAimMode"/>). A thick ray is cast along the aim; the first Swappable it
    /// reaches is the target. Solid things in Sight Blockers stop the aim along its centre line only, so the thickness
    /// makes objects easier to hit without letting walls hide them. The aimed target is never replaced by an object
    /// behind it: if it is rejected, the swap is rejected and the reason is reported.</para>
    ///
    /// <para><b>Rules</b>, checked for the aimed target: within range, not locked, nothing in Sight Blockers between the
    /// two (leave glass out of that mask to swap across glass), and both bodies fit at their new positions. Bodies trade
    /// standing points, so a tall player and a short crate both keep their feet on the other's floor.</para>
    ///
    /// <para><b>Gesture</b>. With Gesture Seconds above 0, a press with a valid target locks it and waits before
    /// swapping, so the hand can close (SwapArmAim). The rules are checked again at the end. The timer lives here, so a
    /// missing animation can never delay or block a swap.</para>
    ///
    /// <para><b>Motion</b> (see <see cref="SwapMotion"/>). Shrink stops game time, shrinks both bodies, trades their
    /// places while tiny and grows them back, keeping their momentum; a player left in the air gets one extra jump.
    /// Instant trades places in one frame and removes all momentum.</para>
    ///
    /// <para><b>Events</b>. <see cref="Swapped"/> fires when a swap is accepted; <see cref="SwapCompleted"/> fires when
    /// both bodies have arrived. For Instant they fire in the same frame.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Swappable))]
    [AddComponentMenu("The Space Between/Swap/Swap Ability")]
    public sealed class SwapAbility : MonoBehaviour
    {
        private const int MaxCandidates = 32;
        private const float ClearanceSkin = 0.02f;
        private const float PointerMovedThreshold = 1f;
        private const float GroundProbeLift = 0.05f;
        private const float GroundProbeLength = 0.15f;

        //==================== INPUT =====================
        [InspectorSection("Input")]
        [Tooltip("Pressing this action attempts a swap.")]
        [SerializeField] private InputAction swapAction = new InputAction("Swap", InputActionType.Button);

        [Tooltip("Aim direction from a stick. Pointer mode only.")]
        [SerializeField] private InputAction aimStickAction = new InputAction("Aim Stick", InputActionType.Value);

        [Tooltip("Screen position of the mouse cursor. Pointer mode only.")]
        [SerializeField] private InputAction pointerAction = new InputAction("Pointer", InputActionType.Value);

        //==================== TARGETING =====================
        [InspectorSection("Targeting")]
        [Tooltip("How the target is chosen. Pointer: mouse or right stick. Facing: the way the player last moved. Nearest: old behaviour, nearest object in any direction.")]
        [SerializeField] private SwapAimMode aimMode = SwapAimMode.Pointer;

        [Tooltip("Thickness of the aim ray in world units, for hitting swappable objects. A thicker ray is more forgiving, a thin one needs a precise aim. Walls block along the ray's centre line only.")]
        [SerializeField, Min(0.05f)] private float aimRadius = 0.35f;

        [Tooltip("How far the stick must be pushed before it counts as aiming.")]
        [ShowIf(nameof(aimMode), SwapAimMode.Pointer)]
        [SerializeField, Range(0.1f, 0.9f)] private float stickDeadZone = 0.3f;

        [Tooltip("Camera used to turn the cursor into a world position. Empty means the main camera.")]
        [ShowIf(nameof(aimMode), SwapAimMode.Pointer)]
        [SerializeField] private Camera aimCamera;

        //==================== RULES =====================
        [InspectorSection("Rules")]
        [Tooltip("Maximum distance to a target, measured between the centres of the two bodies, in world units.")]
        [SerializeField, Min(0.1f)] private float range = 8f;

        [Tooltip("Layers that can hold a Swappable. Keep this narrow so the range check stays cheap.")]
        [SerializeField] private LayerMask swappableLayers = ~0;

        [Tooltip("Layers that block the view between the two bodies. Leave glass out of this mask so glass does not block a swap.")]
        [SerializeField] private LayerMask sightBlockers = ~0;

        [Tooltip("Layers that count as solid when checking whether a body fits at its new position.")]
        [SerializeField] private LayerMask clearanceLayers = ~0;

        //==================== GESTURE =====================
        [InspectorSection("Gesture")]
        [Tooltip("Seconds between pressing swap and the swap happening, for the hand to close. 0 swaps on the press.")]
        [SerializeField, Min(0f)] private float gestureSeconds;

        //==================== MOTION =====================
        [InspectorSection("Motion")]
        [Tooltip("Shrink: game time stops, both bodies shrink, trade places and grow back. Instant: they trade places in one frame.")]
        [SerializeField] private SwapMotion motion = SwapMotion.Shrink;

        [Tooltip("Seconds both bodies take to shrink. Real time, not game time.")]
        [ShowIf(nameof(motion), SwapMotion.Shrink)]
        [SerializeField, Min(0.01f)] private float shrinkTime = 0.15f;

        [Tooltip("Seconds both bodies take to grow back. Real time, not game time.")]
        [ShowIf(nameof(motion), SwapMotion.Shrink)]
        [SerializeField, Min(0.01f)] private float growTime = 0.15f;

        [Tooltip("Size at the smallest point, as a fraction of normal size. Never zero, so colliders stay valid. 1 keeps both bodies at full size and leaves the swap to the displacement silhouettes.")]
        [ShowIf(nameof(motion), SwapMotion.Shrink)]
        [SerializeField, Range(0.01f, 1f)] private float smallestScale = 0.05f;

        [Tooltip("How quickly the size changes over each phase. A slow start and end looks smoother.")]
        [ShowIf(nameof(motion), SwapMotion.Shrink)]
        [SerializeField] private AnimationCurve easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        //==================== EVENTS =====================
        [InspectorSection("Events", false)]
        [Tooltip("Invoked when a swap is accepted.")]
        [SerializeField] private UnityEvent swappedEvent;

        [Tooltip("Invoked when both bodies have arrived and game time runs again.")]
        [SerializeField] private UnityEvent swapCompletedEvent;

        [Tooltip("Invoked when a swap attempt is rejected.")]
        [SerializeField] private UnityEvent rejectedEvent;

        //==================== DEBUG =====================
        [InspectorSection("Debug", false)]
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

        // Targeting state
        private Swappable self;
        private IFacingSource facingSource;
        private Vector3 aimDirection = Vector3.right;
        private Vector3 aimEnd;
        private Vector2 lastPointer;
        private bool stickWasLastUsed;

        // Gesture state
        private Swappable gestureTarget;
        private float gestureElapsed;

        // Motion state
        private bool isSwapping;
        private Coroutine running;
        private Swappable motionTarget;
        private float timeScaleBefore = 1f;
        private Vector3 selfFullScale;
        private Vector3 targetFullScale;

        private readonly Collider[] candidateBuffer = new Collider[MaxCandidates];
        private readonly Collider[] clearanceBuffer = new Collider[MaxCandidates];
        private readonly Swappable[] candidates = new Swappable[MaxCandidates];
        private readonly RaycastHit[] castBuffer = new RaycastHit[MaxCandidates];

        //==================== OUTPUTS =====================
        /// <summary>Raised when a swap is accepted. The argument is the object swapped with.</summary>
        public event Action<Swappable> Swapped;

        /// <summary>Raised when both bodies have arrived. For Instant motion, in the same frame as <see cref="Swapped"/>.</summary>
        public event Action<Swappable> SwapCompleted;

        /// <summary>Raised when an attempt is rejected, with the reason.</summary>
        public event Action<SwapRejection> SwapRejected;

        public SwapRejection LastResult => lastResult;

        public float Range => range;

        /// <summary>How an accepted swap plays out. Change it only while no swap is playing.</summary>
        public SwapMotion Motion
        {
            get => motion;
            set => motion = value;
        }

        /// <summary>True while a Shrink swap is playing. New attempts are ignored until it ends.</summary>
        public bool IsSwapping => isSwapping;

        /// <summary>How long a swap takes from acceptance to arrival, in real time. 0 for Instant.</summary>
        public float TotalDuration => motion == SwapMotion.Shrink ? shrinkTime + growTime : 0f;

        /// <summary>True from an accepted swap press until the fist has closed and the swap happens.</summary>
        public bool IsGesturing => gestureTarget != null;

        /// <summary>How far through the gesture the hand should be: 0 at the press, 1 at the moment of the swap.</summary>
        public float GestureProgress => IsGesturing && gestureSeconds > 0f ? Mathf.Clamp01(gestureElapsed / gestureSeconds) : 0f;

        /// <summary>What the player is reaching for: the locked target during a gesture, otherwise the aimed target. Null while a swap plays.</summary>
        public Swappable FocusTarget => IsGesturing ? gestureTarget : Target;

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
            swapAction = new InputAction("Swap", InputActionType.Button, "<Mouse>/leftButton");
            swapAction.AddBinding("<Gamepad>/buttonWest");

            aimStickAction = new InputAction("Aim Stick", InputActionType.Value, "<Gamepad>/rightStick");
            pointerAction = new InputAction("Pointer", InputActionType.Value, "<Mouse>/position");
        }

        private void Awake()
        {
            self = GetComponent<Swappable>();
            facingSource = GetComponent<IFacingSource>();
        }

        private void OnEnable()
        {
            swapAction.performed += OnSwapPerformed;
            swapAction.Enable();
            aimStickAction.Enable();
            pointerAction.Enable();
        }

        private void OnDisable()
        {
            swapAction.performed -= OnSwapPerformed;
            swapAction.Disable();
            aimStickAction.Disable();
            pointerAction.Disable();
            gestureTarget = null;

            if (isSwapping)
            {
                // Never leave the world shrunk, frozen or with game time stopped.
                if (running != null)
                {
                    StopCoroutine(running);
                    running = null;
                }

                EndShrinkMotion();
            }
        }

        private void Update()
        {
            if (!isSwapping)
            {
                RefreshTargeting();
            }

            if (IsGesturing)
            {
                UpdateGesture();
            }
        }

        //==================== PUBLIC API =====================
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
            if (isSwapping || IsGesturing)
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

        //==================== INPUT AND GESTURE =====================
        private void OnSwapPerformed(InputAction.CallbackContext context)
        {
            if (gestureSeconds <= 0f)
            {
                TrySwap();
                return;
            }

            BeginGesture();
        }

        // A press that cannot work is rejected straight away instead of closing the hand on nothing.
        private void BeginGesture()
        {
            if (isSwapping || IsGesturing)
            {
                return;
            }

            RefreshTargeting();

            if (currentTarget == null || currentStatus != SwapRejection.None)
            {
                Finish(currentStatus, null);
                return;
            }

            gestureTarget = currentTarget;
            gestureElapsed = 0f;
        }

        // Runs on game time, so pausing the game pauses the gesture too.
        private void UpdateGesture()
        {
            gestureElapsed += Time.deltaTime;
            if (gestureElapsed < gestureSeconds)
            {
                return;
            }

            Swappable chosen = gestureTarget;
            gestureTarget = null;

            if (!chosen.isActiveAndEnabled || Vector3.Distance(self.Center, chosen.Center) > range)
            {
                Finish(SwapRejection.NoTargetInRange, null);
                return;
            }

            // Checked again: the target or the player has been moving during the gesture.
            SwapRejection result = Evaluate(chosen);
            if (result != SwapRejection.None)
            {
                Finish(result, null);
                return;
            }

            Execute(chosen);
            Finish(SwapRejection.None, chosen);
        }

        //==================== TARGETING =====================
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

            // With no stick input the cursor sets the aim. If the stick was used last and the cursor has not moved,
            // the stick's aim is kept.
            Vector2 pointer = pointerAction.ReadValue<Vector2>();
            bool pointerMoved = (pointer - lastPointer).sqrMagnitude > PointerMovedThreshold;
            lastPointer = pointer;

            if (stickWasLastUsed && !pointerMoved)
            {
                return;
            }

            Camera view = aimCamera != null ? aimCamera : Camera.main;
            if (view == null)
            {
                return;
            }

            stickWasLastUsed = false;
            Vector3 origin = self.Center;
            Plane gamePlane = new Plane(Vector3.forward, origin);
            Ray ray = view.ScreenPointToRay(pointer);
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

            // Solid things end the aim along its centre line, the same line the sight rule checks. If the thickness
            // also counted for walls, a ledge corner that the centre line clears would hide a crate standing on it.
            float blockedAt = DistanceToSightBlocker(origin, out Vector3 blockedPoint);

            int hits = Physics.SphereCastNonAlloc(origin, aimRadius, aimDirection, castBuffer, range, swappableLayers, QueryTriggerInteraction.Ignore);
            SortHitsByDistance(hits);

            for (int i = 0; i < hits; i++)
            {
                Swappable owner = castBuffer[i].collider.GetComponentInParent<Swappable>();
                if (owner == null || owner == self)
                {
                    continue;
                }

                if (castBuffer[i].distance > blockedAt)
                {
                    break;
                }

                if (Vector3.Distance(origin, owner.Center) > range)
                {
                    return;
                }

                currentTarget = owner;
                currentStatus = Evaluate(owner);
                aimEnd = owner.Center;
                return;
            }

            if (blockedAt < range)
            {
                currentStatus = SwapRejection.LineOfSightBlocked;
                aimEnd = blockedPoint;
            }
        }

        // How far along the aim's centre line the first solid, non swappable thing is. Glass is left out of the mask.
        private float DistanceToSightBlocker(Vector3 origin, out Vector3 point)
        {
            point = origin + aimDirection * range;
            int hits = Physics.RaycastNonAlloc(origin, aimDirection, castBuffer, range, sightBlockers, QueryTriggerInteraction.Ignore);
            float nearest = range;

            for (int i = 0; i < hits; i++)
            {
                if (castBuffer[i].distance >= nearest || castBuffer[i].collider.GetComponentInParent<Swappable>() != null)
                {
                    continue;
                }

                nearest = castBuffer[i].distance;
                point = castBuffer[i].point;
            }

            return nearest;
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
            if (!Physics.Linecast(self.Center, target.Center, out RaycastHit hit, sightBlockers, QueryTriggerInteraction.Ignore))
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

        /// <summary>How far this body moves to trade standing points with the target. The target moves the opposite way.</summary>
        private Vector3 SelfShiftTo(Swappable target)
        {
            return target.Anchor - self.Anchor;
        }

        /// <summary>Whether something solid is directly below the bottom of the given body.</summary>
        private bool IsStandingOnSomething(Swappable body)
        {
            Vector3 origin = body.Anchor + Vector3.up * GroundProbeLift;
            return Physics.Raycast(origin, Vector3.down, GroundProbeLength, clearanceLayers, QueryTriggerInteraction.Ignore);
        }

        private bool Finish(SwapRejection result, Swappable target)
        {
            lastResult = result;

            if (logAttempts)
            {
                string detail = result == SwapRejection.None && target != null ? $" with '{target.name}'" : string.Empty;
                Debug.Log($"[{nameof(SwapAbility)}] '{name}': {SwapRejectionText.Describe(result)}{detail}", this);
            }

            if (result != SwapRejection.None)
            {
                SwapRejected?.Invoke(result);
                rejectedEvent?.Invoke();
                return false;
            }

            Swapped?.Invoke(target);
            swappedEvent?.Invoke();

            // An instant swap has already arrived; a Shrink swap reports its arrival when the motion ends.
            if (!isSwapping)
            {
                RaiseCompleted(target);
            }

            return true;
        }

        private void RaiseCompleted(Swappable target)
        {
            SwapCompleted?.Invoke(target);
            swapCompletedEvent?.Invoke();
        }

        //==================== MOTION =====================
        private void Execute(Swappable target)
        {
            Vector3 shiftOfSelf = SelfShiftTo(target);

            if (motion == SwapMotion.Instant)
            {
                self.MoveTo(self.Position + shiftOfSelf);
                target.MoveTo(target.Position - shiftOfSelf);
                Physics.SyncTransforms();
                return;
            }

            running = StartCoroutine(PlayShrinkMotion(target, shiftOfSelf));
        }

        private IEnumerator PlayShrinkMotion(Swappable target, Vector3 shiftOfSelf)
        {
            isSwapping = true;
            motionTarget = target;
            selfFullScale = self.transform.localScale;
            targetFullScale = target.transform.localScale;

            timeScaleBefore = Time.timeScale;
            Time.timeScale = 0f;

            self.Freeze();
            target.Freeze();

            yield return AnimateScale(1f, smallestScale, shrinkTime);

            // The bodies are tiny now. Move them and keep their momentum.
            self.MoveTo(self.Position + shiftOfSelf, true);
            target.MoveTo(target.Position - shiftOfSelf, true);
            Physics.SyncTransforms();

            yield return AnimateScale(smallestScale, 1f, growTime);

            running = null;
            EndShrinkMotion();

            if (!IsStandingOnSomething(self))
            {
                GrantAirJumpToSelf();
            }

            RaiseCompleted(target);
        }

        // Runs on unscaled time, because game time is stopped for the whole motion.
        private IEnumerator AnimateScale(float from, float to, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float eased = easing.Evaluate(Mathf.Clamp01(elapsed / seconds));
                ApplyScale(Mathf.LerpUnclamped(from, to, eased));
                yield return null;
            }

            ApplyScale(to);
        }

        private void ApplyScale(float factor)
        {
            if (self != null)
            {
                self.transform.localScale = selfFullScale * factor;
            }

            if (motionTarget != null)
            {
                motionTarget.transform.localScale = targetFullScale * factor;
            }
        }

        // Puts sizes back, releases both bodies and restarts game time.
        private void EndShrinkMotion()
        {
            if (self != null)
            {
                self.transform.localScale = selfFullScale;
                self.Unfreeze();
            }

            if (motionTarget != null)
            {
                motionTarget.transform.localScale = targetFullScale;
                motionTarget.Unfreeze();
            }

            Time.timeScale = timeScaleBefore;
            motionTarget = null;
            isSwapping = false;
            Physics.SyncTransforms();
        }

        private void GrantAirJumpToSelf()
        {
            IAirJumpGrantable[] grantables = GetComponents<IAirJumpGrantable>();
            for (int i = 0; i < grantables.Length; i++)
            {
                grantables[i].GrantAirJump();
            }
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
            {
                return;
            }

            Swappable own = GetComponent<Swappable>();
            Vector3 center = own != null ? own.Center : transform.position;

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
