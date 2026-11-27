using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// The still figure inside a placeholder containment chamber: a copy of the Protagonist model held in its standing idle,
    /// slowed down, as a dark silhouette. It tells the Animator the figure is standing, so it never plays the fall.
    /// Part of the placeholder chamber only; the final containment tanks replace it.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class ChamberSubject : MonoBehaviour
    {
        [Tooltip("Playback speed of the idle, so the figure barely breathes.")]
        [SerializeField, Range(0f, 1f)] private float idleSpeed = 0.35f;

        private void Start()
        {
            Animator animator = GetComponent<Animator>();
            animator.SetBool("IsGrounded", true);
            animator.SetFloat("Speed", 0f);
            animator.speed = idleSpeed;
        }
    }
}
