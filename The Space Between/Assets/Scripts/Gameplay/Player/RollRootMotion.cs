using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Carries the player along with the landing roll. The roll clip tumbles forward about 2.5 m, but the Animator never
    /// moves the player itself (the PlaceholderPlayerController does), so without this the body rolled on the spot where
    /// it landed. While the Animator is in the Roll state, this hands the clip's own forward motion to the movement, so the
    /// player rolls on the way it was facing as it fell, and the travel matches the tumble. Leaving the roll blends that
    /// motion out with the transition. Every other state still ignores the clips' motion, as before.
    ///
    /// PlayerAnimationDriver adds this to the object with the Animator by itself; nothing needs setting up.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public class RollRootMotion : MonoBehaviour
    {
        private Animator animator;
        private PlaceholderPlayerController movement;
        private int rollStateHash;

        /// <summary>Starts carrying the player with the given state's motion.</summary>
        public void Bind(PlaceholderPlayerController playerMovement, int stateShortNameHash)
        {
            animator = GetComponent<Animator>();
            movement = playerMovement;
            rollStateHash = stateShortNameHash;
        }

        // Having this method also stops the Animator from moving its own object, exactly like Apply Root Motion off.
        private void OnAnimatorMove()
        {
            if (movement == null || !IsInRoll())
            {
                return;
            }

            movement.MoveByAnimation(animator.deltaPosition, Time.deltaTime);
        }

        private bool IsInRoll()
        {
            return animator.GetCurrentAnimatorStateInfo(0).shortNameHash == rollStateHash
                   || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == rollStateHash);
        }
    }
}
