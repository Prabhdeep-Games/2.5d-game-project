using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>Shows its target only when a narrative flag is set, for example the lit window of the exterior.</summary>
    public sealed class ShowWhenFlag : MonoBehaviour
    {
        [Tooltip("The flag to read, such as SawOccupiedCell.")]
        [SerializeField] private string flag = NarrativeFlags.SawOccupiedCell;

        [Tooltip("Shown when the flag is set, hidden otherwise.")]
        [SerializeField] private GameObject target;

        private void Awake()
        {
            if (target != null)
            {
                target.SetActive(NarrativeFlags.Get(flag));
            }
        }
    }
}
