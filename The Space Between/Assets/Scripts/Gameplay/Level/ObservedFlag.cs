using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Remembers that the player looked through an Observation Point, as a narrative flag kept with the saved progress.
    /// The Containment Wing's lit cell uses it to set SawOccupiedCell, which the exterior reads (ShowWhenFlag).
    /// </summary>
    public sealed class ObservedFlag : MonoBehaviour
    {
        [Tooltip("The Observation Point that counts. Empty means the one on this object.")]
        [SerializeField] private ObservationPoint point;

        [Tooltip("The flag set the first time the player observes through it.")]
        [SerializeField] private string flag = NarrativeFlags.SawOccupiedCell;

        public string Flag => flag;

        private void Awake()
        {
            if (point == null)
            {
                point = GetComponent<ObservationPoint>();
            }
        }

        private void Update()
        {
            if (point != null && point.IsObserving && !string.IsNullOrEmpty(flag))
            {
                NarrativeFlags.Set(flag);
                enabled = false;
            }
        }
    }
}
