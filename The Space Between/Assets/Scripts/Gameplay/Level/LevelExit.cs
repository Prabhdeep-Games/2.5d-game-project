using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheSpaceBetween
{
    /// <summary>
    /// The way out of a level. When the player's middle enters the Box Collider on this object, the next level loads
    /// behind the Facility UI's loading screen, starting at its first room. It works once, and waits while a
    /// "position lost" return, the pause menu or a cutscene is running, so completing a level and failing can never
    /// both happen. Put it just past the level's exit door, so the player walks into the doorway as the screen fades.
    ///
    /// The collider should be a trigger on the Ignore Raycast layer. Without a Facility UI in the scene, the next
    /// scene loads directly.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class LevelExit : MonoBehaviour
    {
        //==================== EXIT =====================
        [Header("Exit")]
        [Tooltip("The scene this exit leads to. It must be in Build Settings.")]
        [SerializeField] private string nextScene;

        [Tooltip("The short line on the loading screen, such as ACCESSING SECTOR. Empty shows only the title.")]
        [SerializeField] private string loadingMessage = "ACCESSING SECTOR";

        [Tooltip("Use the pale loading screen, for stepping out of the building into daylight.")]
        [SerializeField] private bool brightTransition;

        [Tooltip("Real seconds between reaching the exit and the fade, for example to let a closing beat register.")]
        [SerializeField, Min(0f)] private float delaySeconds;

        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private Transform player;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Whether the exit has been used.")]
        [ReadOnly, SerializeField] private bool used;

        private BoxCollider area;
        private float reachedAt = -1f;

        public bool Used => used;

        public string NextScene => nextScene;

        public string LoadingMessage => loadingMessage;

        public bool BrightTransition => brightTransition;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        private void Awake()
        {
            area = GetComponent<BoxCollider>();
            if (player == null)
            {
                PlaceholderPlayerController controller = FindAnyObjectByType<PlaceholderPlayerController>();
                player = controller != null ? controller.transform : null;
            }
        }

        private void Update()
        {
            if (used || player == null || string.IsNullOrEmpty(nextScene))
            {
                return;
            }

            if (reachedAt < 0f)
            {
                if (!area.bounds.Contains(player.position + Vector3.up))
                {
                    return;
                }

                reachedAt = Time.unscaledTime;
            }

            if (Time.unscaledTime - reachedAt < delaySeconds)
            {
                return;
            }

            FacilityUI ui = FacilityUI.Current;
            if (ui == null)
            {
                used = true;
                SceneManager.LoadScene(nextScene);
                return;
            }

            if (ui.IsReturning || ui.IsPaused || ui.IsLeaving || ui.IsEnding || ui.IsCutscene)
            {
                return;
            }

            used = true;
            ui.LoadScene(nextScene, loadingMessage, 0, brightTransition);
        }

        private void OnDrawGizmos()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null)
            {
                return;
            }

            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
