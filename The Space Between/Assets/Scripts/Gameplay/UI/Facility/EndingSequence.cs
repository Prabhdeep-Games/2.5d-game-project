using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// The quiet ending, played once when the player reaches the Level Goal. Control is taken away and the view settles
    /// on a still, wide camera, so the facility fills the frame and the protagonist is small within it. After a pause
    /// the title appears, then END under it; only then does the view darken for the credits, which lead back to the
    /// main menu.
    ///
    /// Put it in the level next to its Level Goal. The Ending View camera is a CinemachineCamera kept off until the end.
    /// </summary>
    public sealed class EndingSequence : MonoBehaviour
    {
        //==================== ENDING =====================
        [Header("Ending")]
        [Tooltip("The goal that starts the ending. Empty means the one in the scene.")]
        [SerializeField] private LevelGoal goal;

        [Tooltip("The still, wide view held for the ending. Off until the goal is reached.")]
        [SerializeField] private CinemachineCamera endingCamera;

        [Tooltip("Music that fades in with the ending. Optional.")]
        [SerializeField] private AudioChannel music;

        [Tooltip("Start when the player reaches the goal. Off when another sequence (such as the drone strike) starts the ending by calling Begin.")]
        [SerializeField] private bool startOnGoal = true;

        //==================== TEXT =====================
        [Header("Text")]
        [SerializeField] private string title = "THE SPACE BETWEEN";
        [SerializeField] private string endText = "END";

        //==================== TIMING =====================
        [Header("Timing")]
        [Tooltip("Seconds of the still view before the title.")]
        [SerializeField, Min(0f)] private float pauseBeforeTitle = 2.5f;

        [SerializeField, Min(0.05f)] private float titleFadeSeconds = 1.6f;

        [Tooltip("Seconds between the title and END.")]
        [SerializeField, Min(0f)] private float endDelay = 2.2f;

        [SerializeField, Min(0.05f)] private float endFadeSeconds = 1.2f;

        [Tooltip("Seconds both lines stay before the credits.")]
        [SerializeField, Min(0f)] private float holdAfterEnd = 3.5f;

        [SerializeField, Min(0.05f)] private float musicFadeSeconds = 4f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [ReadOnly, SerializeField] private string stage = "Waiting";

        private bool started;

        public string Stage => stage;

        private void Awake()
        {
            if (goal == null)
            {
                goal = FindAnyObjectByType<LevelGoal>();
            }

            if (endingCamera != null)
            {
                endingCamera.enabled = false;
            }

            if (music != null)
            {
                music.BaseVolume = 0f;
            }
        }

        private void Update()
        {
            if (startOnGoal && !started && goal != null && goal.Reached)
            {
                Begin();
            }
        }

        /// <summary>Starts the ending now. Does nothing if it has already started.</summary>
        public void Begin()
        {
            if (started)
            {
                return;
            }

            started = true;
            StartCoroutine(Play());
        }

        private IEnumerator Play()
        {
            FacilityUI ui = FacilityUI.Current;
            if (ui == null || (!ui.IsEnding && !ui.BeginEnding()))
            {
                stage = "No Facility UI";
                yield break;
            }

            stage = "Still view";
            if (endingCamera != null)
            {
                endingCamera.enabled = true;
                endingCamera.Prioritize();
            }

            if (music != null)
            {
                if (!music.Source.isPlaying)
                {
                    music.Source.Play();
                }

                StartCoroutine(FadeMusicIn());
            }

            yield return Wait(pauseBeforeTitle);
            stage = "Title";
            ui.ShowMessage(title, null, titleFadeSeconds, true);
            yield return Wait(titleFadeSeconds + endDelay);
            stage = "End";
            ui.ShowSubtitle(endText, endFadeSeconds);
            yield return Wait(endFadeSeconds + holdAfterEnd);

            stage = "Credits";
            ui.HideMessage(1.2f);
            ui.DimView(1f, 2f);
            yield return Wait(1.5f);
            ui.OpenCredits(ui.GoToMainMenu);
        }

        private IEnumerator FadeMusicIn()
        {
            float start = Time.unscaledTime;
            while (music != null && Time.unscaledTime - start < musicFadeSeconds)
            {
                music.BaseVolume = (Time.unscaledTime - start) / musicFadeSeconds;
                yield return null;
            }

            if (music != null)
            {
                music.BaseVolume = 1f;
            }
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until)
            {
                yield return null;
            }
        }
    }
}
