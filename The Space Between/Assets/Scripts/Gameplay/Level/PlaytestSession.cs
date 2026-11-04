using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace TheSpaceBetween
{
    /// <summary>
    /// The observer's keys during a playtest. Nothing here shows on screen or helps the player while they play.
    ///
    /// The restart key reloads the scene, so everything returns to its saved state and the next participant starts
    /// under the same conditions: player, swaps, obstacle timing, doors and camera. It also caps the frame rate, so the
    /// game runs the same way on any computer.
    ///
    /// The section keys put the player at the start of a section, using that section's own RoomReset, so a participant
    /// who is stuck can still reach the mechanics after it. Each use changes what the session shows, so note it down.
    ///
    /// Put this on one empty object in the playtest scene. The scene must be in Build Settings for the restart to work.
    /// </summary>
    public class PlaytestSession : MonoBehaviour
    {
        private const int NoFrameRateCap = -1;

        //==================== KEYS =====================
        [Header("Keys")]
        [Tooltip("Reloads the scene for a clean start.")]
        [SerializeField] private Key restartKey = Key.F12;

        [Tooltip("Moves the player to the first section. The next function keys move to the sections after it.")]
        [SerializeField] private Key firstSectionKey = Key.F1;

        //==================== SECTIONS =====================
        [Header("Sections")]
        [Tooltip("The room of each section, in the order of the route.")]
        [SerializeField] private RoomReset[] sections = new RoomReset[0];

        [Tooltip("The gameplay camera. It cuts to the player after a section key instead of travelling along the level.")]
        [SerializeField] private CinemachineCamera followCamera;

        //==================== CONDITIONS =====================
        [Header("Conditions")]
        [Tooltip("Frame rate every participant plays at, whatever their computer can reach. Uncapped, a fast machine runs at about 1000, which the route has never been tested at.")]
        [SerializeField, Min(30)] private int frameRate = 60;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write each restart and section change to the Console.")]
        [SerializeField] private bool logActions = true;

        [Tooltip("How many times a section key has been used since the last restart.")]
        [ReadOnly, SerializeField] private int sectionKeysUsed;

        public int SectionKeysUsed => sectionKeysUsed;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = frameRate;
        }

        // The cap belongs to the playtest, so it must not stay on for a scene loaded after this one.
        private void OnDestroy()
        {
            Application.targetFrameRate = NoFrameRateCap;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard[restartKey].wasPressedThisFrame)
            {
                Restart();
                return;
            }

            for (int i = 0; i < sections.Length; i++)
            {
                if (keyboard[firstSectionKey + i].wasPressedThisFrame)
                {
                    GoToSection(i);
                    return;
                }
            }
        }

        //==================== ACTIONS =====================
        /// <summary>Reloads the scene. Game time is restored first, because a smooth swap stops it while it plays.</summary>
        public void Restart()
        {
            Time.timeScale = 1f;
            if (logActions)
            {
                Debug.Log($"[{nameof(PlaytestSession)}] Session restarted.", this);
            }

            SceneManager.LoadScene(gameObject.scene.name);
        }

        /// <summary>Puts the player at the start of a section and puts that section's objects back.</summary>
        public void GoToSection(int index)
        {
            if (index < 0 || index >= sections.Length || sections[index] == null)
            {
                return;
            }

            sections[index].ResetRoom();
            sectionKeysUsed++;
            if (followCamera != null)
            {
                followCamera.PreviousStateIsValid = false;
            }

            if (logActions)
            {
                Debug.Log($"[{nameof(PlaytestSession)}] Moved to section {index + 1}, '{sections[index].name}'.", this);
            }
        }
    }
}
