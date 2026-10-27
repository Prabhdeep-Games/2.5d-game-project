using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Marks a surface the player can run up. Put it on a ramp, a tall wall or a curved wall.
    /// A steep surface without this component is just a wall, so level designers decide
    /// exactly which models can be run on.
    /// </summary>
    public class WallRunSurface : MonoBehaviour
    {
        //==================== SETTINGS =====================
        [Header("Settings")]
        [Tooltip("Turn off to make this surface an ordinary wall again without removing the component.")]
        [SerializeField] private bool allowWallRun = true;

        public bool AllowWallRun => allowWallRun;
    }
}
