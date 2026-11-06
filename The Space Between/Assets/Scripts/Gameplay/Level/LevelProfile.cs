using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// What kind of level a scene is. Add one to a scene; the Room Generator reads it, and game code can too.
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelProfile : MonoBehaviour
    {
        [Tooltip("On: a puzzle-focused level, where doors, glass and cameras are meant to stop the player until a puzzle is solved. Off: a traversal level built for flowing movement, where the Room Generator warns about anything that interrupts the run. It never changes or moves what is in the scene.")]
        [SerializeField] private bool isPuzzleLevel;

        /// <summary>Whether this scene is a puzzle-focused level rather than a traversal level.</summary>
        public bool IsPuzzleLevel => isPuzzleLevel;
    }
}
