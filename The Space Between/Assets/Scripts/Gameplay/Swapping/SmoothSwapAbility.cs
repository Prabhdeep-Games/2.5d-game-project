using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace TheSpaceBetween
{
    /// <summary>
    /// A swap that plays out as a short animation instead of an instant teleport.
    ///
    /// When a swap is accepted:
    ///   1. Game time stops (Time.timeScale becomes 0). The animation runs on unscaled time.
    ///   2. Both bodies shrink at their current positions.
    ///   3. They trade positions while tiny.
    ///   4. Both grow back to full size at the new positions.
    ///   5. Game time resumes and each body carries on with the momentum it had before.
    ///
    /// If the player ends up in the air after the swap, it receives one extra jump (see
    /// IAirJumpGrantable). The extra jump is lost on landing.
    ///
    /// Target selection and all the rules (range, line of sight, clearance) are inherited
    /// from SwapAbility.
    /// </summary>
    public class SmoothSwapAbility : SwapAbility
    {
        //==================== ANIMATION =====================
        [Header("Animation")]
        [Tooltip("Seconds both bodies take to shrink. Real time, not game time.")]
        [SerializeField, Min(0.01f)] private float shrinkTime = 0.15f;

        [Tooltip("Seconds both bodies take to grow back. Real time, not game time.")]
        [SerializeField, Min(0.01f)] private float growTime = 0.15f;

        [Tooltip("Size at the smallest point, as a fraction of normal size. Never zero, so colliders stay valid.")]
        [SerializeField, Range(0.01f, 0.5f)] private float smallestScale = 0.05f;

        [Tooltip("How quickly the size changes over each phase. A slow start and end looks smoother.")]
        [SerializeField] private AnimationCurve easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked when both bodies have arrived and game time has resumed.")]
        [SerializeField] private UnityEvent swapCompletedEvent;

        private bool isSwapping;
        private float timeScaleBefore = 1f;
        private Coroutine running;
        private Swappable activeTarget;
        private Vector3 selfFullScale;
        private Vector3 targetFullScale;

        //==================== OUTPUTS =====================
        /// <summary>Raised when the animation has finished. The argument is the object swapped with.</summary>
        public event Action<Swappable> SwapCompleted;

        public override bool IsSwapping => isSwapping;

        //==================== LIFECYCLE =====================
        protected override void OnDisable()
        {
            base.OnDisable();

            if (isSwapping)
            {
                // Leave the world as it was: normal size, unfrozen, and game time running.
                if (running != null)
                {
                    StopCoroutine(running);
                    running = null;
                }

                RestoreWorld();
            }
        }

        //==================== SWAP =====================
        protected override void Execute(Swappable target)
        {
            Vector3 shiftOfSelf = SelfShiftTo(target);
            running = StartCoroutine(PlaySwap(target, shiftOfSelf));
        }

        private IEnumerator PlaySwap(Swappable target, Vector3 shiftOfSelf)
        {
            isSwapping = true;
            activeTarget = target;
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
            RestoreWorld();

            if (!IsStandingOnSomething(self))
            {
                GrantAirJumpToSelf();
            }

            SwapCompleted?.Invoke(target);
            swapCompletedEvent?.Invoke();
        }

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

            if (activeTarget != null)
            {
                activeTarget.transform.localScale = targetFullScale * factor;
            }
        }

        // Puts sizes back, releases both bodies, and restarts game time.
        private void RestoreWorld()
        {
            if (self != null)
            {
                self.transform.localScale = selfFullScale;
                self.Unfreeze();
            }

            if (activeTarget != null)
            {
                activeTarget.transform.localScale = targetFullScale;
                activeTarget.Unfreeze();
            }

            Time.timeScale = timeScaleBefore;
            activeTarget = null;
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
    }
}
