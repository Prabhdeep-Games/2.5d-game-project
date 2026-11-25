using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Moves this object very slowly back and forth along a line and back, like a camera on a long, patient track.
    /// Used for the main menu view. Runs on real time.
    /// </summary>
    public sealed class SlowDrift : MonoBehaviour
    {
        [Tooltip("How far the object travels from where it starts, in world units.")]
        [SerializeField] private Vector3 travel = new Vector3(0f, 0f, 1.5f);

        [Tooltip("Seconds for one trip out and back.")]
        [SerializeField, Min(1f)] private float period = 60f;

        private Vector3 start;

        private void Start()
        {
            start = transform.position;
        }

        private void Update()
        {
            float t = (1f - Mathf.Cos(Time.unscaledTime / period * Mathf.PI * 2f)) * 0.5f;
            transform.position = start + travel * t;
        }
    }
}
