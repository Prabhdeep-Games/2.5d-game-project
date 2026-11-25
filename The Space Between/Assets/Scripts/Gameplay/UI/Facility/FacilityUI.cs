using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TheSpaceBetween
{
    /// <summary>
    /// The game's interface, styled as part of the facility: pause menu, settings, credits, the loading screen, the
    /// "position lost" return after a hazard and the titles of the ending. One per scene, on the Facility UI prefab.
    ///
    /// Everything is drawn by UI Toolkit with one shared style sheet (Assets/UI/Facility/FacilityUI.uss). Nothing is
    /// on screen while the level is played; the interface appears only when it has something to say.
    ///
    /// It reuses the game's own systems: the hazard reset (RoomReset) for the return to the last room, the
    /// Playtest Session's sections for saving progress, the player's control lock (as observation does) for pausing,
    /// and a Color Adjustments volume override to dim the paused view. Fades run on real time, so they keep going
    /// while the game is paused or a swap has stopped game time.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class FacilityUI : MonoBehaviour
    {
        private sealed class Arrival
        {
            public string Message;
            public int Room = -1;
        }

        private sealed class Fade
        {
            public VisualElement Element;
            public float Target;
            public float Speed;
        }

        // A scene loaded through the loading screen arrives with it still up; the new scene's Facility UI takes it down.
        private static Arrival arrival;

        //==================== LOOK =====================
        [Header("Look")]
        [Tooltip("The shared style sheet of every screen.")]
        [SerializeField] private StyleSheet styleSheet;

        //==================== SCENES =====================
        [Header("Scenes")]
        [Tooltip("The main menu scene. Must be in Build Settings.")]
        [SerializeField] private string menuScene = "MainMenu";

        [Tooltip("The level New Game and Continue load. Must be in Build Settings.")]
        [SerializeField] private string gameScene = "Final_Playtest";

        //==================== GAMEPLAY =====================
        [Header("Gameplay")]
        [Tooltip("Escape or the gamepad Start button opens the pause menu. Off in the main menu.")]
        [SerializeField] private bool allowPause = true;

        [Tooltip("Save the furthest room reached, so Continue can start there.")]
        [SerializeField] private bool recordProgress = true;

        [Tooltip("Hazards play the 'position lost' return instead of resetting the room at once.")]
        [SerializeField] private bool positionLostOnHazard = true;

        //==================== POSITION LOST =====================
        [Header("Position Lost")]
        [Tooltip("The one line shown after a hazard.")]
        [SerializeField] private string positionLostText = "POSITION LOST";

        [Tooltip("Seconds from the hit to full dark. The room is reset in the dark.")]
        [SerializeField, Min(0.05f)] private float darkenSeconds = 0.35f;

        [Tooltip("Seconds the line stays up in the dark.")]
        [SerializeField, Min(0f)] private float holdSeconds = 0.9f;

        [Tooltip("Seconds from dark back to the room.")]
        [SerializeField, Min(0.05f)] private float returnSeconds = 0.5f;

        [Tooltip("Seconds of the short fade when a room is restarted from the pause menu.")]
        [SerializeField, Min(0.05f)] private float checkpointFadeSeconds = 0.25f;

        [Tooltip("Seconds of the brief dip to dark when a room is reset with its key or a section key.")]
        [SerializeField, Min(0f)] private float resetBlinkSeconds = 0.35f;

        //==================== LOADING =====================
        [Header("Loading")]
        [Tooltip("Shown while New Game loads the level.")]
        [SerializeField] private string newGameMessage = "ACCESSING SECTOR";

        [Tooltip("Shown while Continue loads the level.")]
        [SerializeField] private string continueMessage = "RECONSTRUCTING SPATIAL DATA";

        [Tooltip("Shown while the main menu loads.")]
        [SerializeField] private string menuMessage = "RESTORING SYSTEMS";

        //==================== CREDITS =====================
        [Header("Credits")]
        [Tooltip("The heading at the top of the credits.")]
        [SerializeField] private string creditsHeading = "The Space Between";

        [Tooltip("One line each. Write 'Role: Name' to show the role small above the name. An empty line leaves a gap.")]
        [SerializeField] private List<string> credits = new List<string>();

        [Tooltip("Shown under the heading while the list is empty.")]
        [SerializeField] private string emptyCreditsNote = "Credits to follow";

        [Tooltip("Scroll speed of the credits, in reference pixels per second (the screen is 1080 tall).")]
        [SerializeField, Min(5f)] private float creditsSpeed = 45f;

        //==================== CONTROLS LIST =====================
        [Header("Controls List")]
        [Tooltip("Read for the controls in Settings when the scene has no player, such as the main menu.")]
        [SerializeField] private PlaceholderPlayerController playerPrefab;

        [Tooltip("Read for the Observe control when the scene has no viewing plate.")]
        [SerializeField] private ObservationPoint observationPrefab;

        //==================== SOUND =====================
        [Header("Sound")]
        [Tooltip("Plays the interface sounds. Put an AudioChannel (Sound Effects) on it.")]
        [SerializeField] private AudioSource interfaceSource;

        [SerializeField] private AudioClip moveClip;
        [SerializeField] private AudioClip confirmClip;
        [SerializeField] private AudioClip backClip;
        [SerializeField] private AudioClip valueUpClip;
        [SerializeField] private AudioClip valueDownClip;
        [SerializeField] private AudioClip positionLostClip;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write pauses, returns and scene changes to the Console.")]
        [SerializeField] private bool logActions;

        [Tooltip("What the interface is doing right now.")]
        [ReadOnly, SerializeField] private string state = "Idle";

        private readonly List<FacilityScreen> stack = new List<FacilityScreen>();
        private readonly List<Fade> fades = new List<Fade>();
        private readonly List<RoomReset> rooms = new List<RoomReset>();

        private MenuInput input;
        private VisualElement root;
        private VisualElement screenLayer;
        private VisualElement fader;
        private VisualElement messageLayer;
        private Label messageTitle;
        private Label messageSubtitle;
        private VisualElement loadingLayer;
        private VisualElement loadingFill;
        private Label loadingMessage;

        private Volume lookVolume;
        private VolumeProfile lookProfile;
        private ColorAdjustments look;
        private float lookTarget;
        private float lookSpeed = 1f;

        private PlaceholderPlayerController player;
        private SwapAbility swap;
        private PlaytestSession session;
        private PauseScreen pauseScreen;
        private SettingsScreen settingsScreen;

        private bool paused;
        private bool pauseRequested;
        private bool failing;
        private bool leaving;
        private bool ending;
        private bool arriving;
        private bool locked;
        private bool controlLockedBefore;
        private bool swapEnabledBefore;
        private float timeScaleBefore = 1f;
        private float loadingProgress;
        private int handledFrame = -1;
        private RoomReset lastRoom;

        public static FacilityUI Current { get; private set; }

        public bool IsPaused => paused;
        public bool IsReturning => failing;
        public bool IsLeaving => leaving;
        public bool IsEnding => ending;

        /// <summary>The name of the screen on top, or null when none is open.</summary>
        public string TopScreen => stack.Count > 0 ? stack[stack.Count - 1].Name : null;

        /// <summary>The line shown in the middle of the screen right now, or null.</summary>
        public string MessageText => messageLayer != null && messageLayer.resolvedStyle.opacity > 0.5f && !string.IsNullOrEmpty(messageTitle.text) ? messageTitle.text : null;

        /// <summary>The second line under the message, or null.</summary>
        public string MessageSubtitle => MessageText != null && messageSubtitle.resolvedStyle.opacity > 0.5f && !string.IsNullOrEmpty(messageSubtitle.text) ? messageSubtitle.text : null;

        public bool LoadingVisible => loadingLayer != null && loadingLayer.style.display.value != DisplayStyle.None && loadingLayer.style.opacity.value > 0.01f;
        public float LoadingProgress => loadingProgress;
        public string LoadingMessage => loadingMessage != null ? loadingMessage.text : null;
        public float LookWeight => lookVolume != null ? lookVolume.weight : 0f;

        public string MenuScene => menuScene;
        public string GameScene => gameScene;

        /// <summary>The options of the screen on top, in order, or null when it has none.</summary>
        public IReadOnlyList<string> Options => stack.Count > 0 ? stack[stack.Count - 1].Menu?.Texts : null;

        /// <summary>The selected option of the screen on top, or the selected settings row.</summary>
        public string SelectedOption => stack.Count == 0 ? null
            : stack[stack.Count - 1] is SettingsScreen settings ? settings.SelectedRow
            : stack[stack.Count - 1].Menu?.SelectedText;

        /// <summary>Chooses an option on the screen on top, as Enter or a click would. False when it is missing or disabled.</summary>
        public bool Choose(string option)
        {
            return stack.Count > 0 && stack[stack.Count - 1].Menu != null && stack[stack.Count - 1].Menu.Choose(option);
        }

        public bool IsOptionEnabled(string option)
        {
            return stack.Count > 0 && stack[stack.Count - 1].Menu != null && stack[stack.Count - 1].Menu.IsEnabled(option);
        }

        /// <summary>Does what Escape does on the screen on top.</summary>
        public void Back()
        {
            if (stack.Count > 0)
            {
                stack[stack.Count - 1].Back();
            }
        }

        /// <summary>The key a Settings controls row shows, such as "W" for Jump. Opens nothing; null before Settings has been shown.</summary>
        public string ControlText(string action, bool gamepad) => settingsScreen?.ControlText(action, gamepad);

        /// <summary>Changes a Settings row as left (-1) or right (1) would.</summary>
        public void StepSetting(string row, int direction) => settingsScreen?.StepRow(row, direction);

        /// <summary>Where the controls list in Settings reads its bindings: the scene's player first, then the prefabs.</summary>
        internal (PlaceholderPlayerController Player, SwapAbility Swap, ObservationPoint Observe) Controls
        {
            get
            {
                PlaceholderPlayerController p = player != null ? player : playerPrefab;
                SwapAbility s = swap != null ? swap : playerPrefab != null ? playerPrefab.GetComponent<SwapAbility>() : null;
                ObservationPoint o = FindAnyObjectByType<ObservationPoint>();
                return (p, s, o != null ? o : observationPrefab);
            }
        }

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            Current = this;
            input = new MenuInput();
            BuildInterface();
            BuildLook();

            GameplayHold.Active = false;
            TutorialGuide.HiddenByInterface = false;
            if (arrival != null)
            {
                arriving = true;
                SetLoadingVisible(arrival.Message, 1f);
            }
        }

        private void OnEnable()
        {
            if (positionLostOnHazard)
            {
                RoomReset.FailureHandler = OnHazard;
            }
        }

        private void Start()
        {
            player = FindAnyObjectByType<PlaceholderPlayerController>();
            swap = player != null ? player.GetComponent<SwapAbility>() : null;
            session = FindAnyObjectByType<PlaytestSession>();
            foreach (RoomReset room in FindObjectsByType<RoomReset>(FindObjectsSortMode.None))
            {
                rooms.Add(room);
                room.WasReset += OnRoomReset;
            }

            GameSettings.ApplyAll();
            StartCoroutine(Arrive());
        }

        private void OnDisable()
        {
            if (RoomReset.FailureHandler == (Func<RoomReset, bool>)OnHazard)
            {
                RoomReset.FailureHandler = null;
            }
        }

        private void OnDestroy()
        {
            foreach (RoomReset room in rooms)
            {
                if (room != null)
                {
                    room.WasReset -= OnRoomReset;
                }
            }

            // Never leave the game stopped or locked behind this object.
            if (paused)
            {
                Time.timeScale = timeScaleBefore;
            }

            if (Current == this)
            {
                Current = null;
                GameplayHold.Active = false;
                TutorialGuide.HiddenByInterface = false;
            }

            input?.Dispose();
            if (lookProfile != null)
            {
                Destroy(lookProfile);
            }
        }

        private void Update()
        {
            input.Update();
            TickFades();
            TickLook();

            if (pauseRequested && (swap == null || !swap.IsSwapping))
            {
                pauseRequested = false;
                OpenPause();
            }

            if (stack.Count > 0 && handledFrame != Time.frameCount)
            {
                stack[stack.Count - 1].HandleInput(input);
            }
            else if (input.Pause && CanPause())
            {
                if (swap != null && swap.IsSwapping)
                {
                    pauseRequested = true;
                }
                else
                {
                    OpenPause();
                }
            }

            TrackRoom();
        }

        //==================== SCREENS =====================
        internal void OpenScreen(FacilityScreen screen)
        {
            if (stack.Count > 0)
            {
                stack[stack.Count - 1].Root.style.display = DisplayStyle.None;
            }

            stack.Add(screen);
            if (screen.Root.parent != screenLayer)
            {
                screenLayer.Add(screen.Root);
            }

            screen.Root.style.display = DisplayStyle.Flex;
            screen.Root.style.opacity = 0f;
            FadeTo(screen.Root, 1f, 0.3f);
            screen.OnShow();
            ConsumeInput();
        }

        internal void CloseScreen(FacilityScreen screen)
        {
            int index = stack.IndexOf(screen);
            if (index < 0)
            {
                return;
            }

            stack.RemoveAt(index);
            screen.Root.style.display = DisplayStyle.None;
            if (stack.Count > 0 && index == stack.Count)
            {
                FacilityScreen below = stack[stack.Count - 1];
                below.Root.style.display = DisplayStyle.Flex;
                below.Root.style.opacity = 0f;
                FadeTo(below.Root, 1f, 0.25f);
                below.OnShow();
            }

            ConsumeInput();
        }

        private void CloseAllScreens()
        {
            foreach (FacilityScreen screen in stack)
            {
                screen.Root.style.display = DisplayStyle.None;
            }

            stack.Clear();
        }

        // One key press must not act on two screens, such as Escape opening the pause menu and closing it again.
        private void ConsumeInput()
        {
            handledFrame = Time.frameCount;
            input.Consume();
        }

        public void OpenSettings()
        {
            if (settingsScreen == null)
            {
                settingsScreen = new SettingsScreen(this);
            }

            OpenScreen(settingsScreen);
        }

        /// <summary>Shows the credits. When they end or are skipped, the callback runs (or the screen just closes).</summary>
        public void OpenCredits(Action finished = null)
        {
            CreditsScreen screen = null;
            screen = new CreditsScreen(this, creditsHeading, credits, emptyCreditsNote, creditsSpeed, () =>
            {
                if (finished != null)
                {
                    finished();
                }
                else
                {
                    PlayBack();
                    CloseScreen(screen);
                }
            });
            OpenScreen(screen);
        }

        //==================== PAUSE =====================
        private bool CanPause()
        {
            return allowPause && player != null && !paused && !failing && !leaving && !ending && !arriving;
        }

        public void OpenPause()
        {
            if (!CanPause())
            {
                return;
            }

            paused = true;
            timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            LockGameplay(true);
            SetLook(-0.7f, -1.1f, 0f, 0.4f, 1f);
            if (pauseScreen == null)
            {
                pauseScreen = new PauseScreen(this);
            }

            OpenScreen(pauseScreen);
            PlayConfirm();
            SetState("Paused");
        }

        public void Resume()
        {
            if (!paused)
            {
                return;
            }

            CloseAllScreens();
            GameSettings.Save();
            paused = false;
            Time.timeScale = timeScaleBefore;
            LockGameplay(false);
            SetLook(0f, 0f, 0f, 0.35f, 0f);
            ConsumeInput();
            SetState("Idle");
        }

        /// <summary>Resumes and puts the player back at the start of the room they are in, with a short fade.</summary>
        public void RestartCheckpoint()
        {
            RoomReset room = CurrentRoom();
            Resume();
            if (room != null)
            {
                StartCoroutine(ReturnToRoom(room, false));
            }
        }

        public void GoToMainMenu()
        {
            LoadScene(menuScene, menuMessage, -1);
        }

        /// <summary>Starts the level from its first room and clears saved progress.</summary>
        public void NewGame()
        {
            GameProgress.Clear();
            LoadScene(gameScene, newGameMessage, 0);
        }

        /// <summary>Loads the level at the furthest room reached. Does nothing without saved progress.</summary>
        public void ContinueGame()
        {
            if (GameProgress.HasSave)
            {
                LoadScene(gameScene, continueMessage, GameProgress.Room);
            }
        }

        public void Quit()
        {
            GameSettings.Save();
            SetState("Quitting");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Stops the player and the swap the same way observation does, and remembers what to give back.
        private void LockGameplay(bool on)
        {
            if (on == locked)
            {
                UpdateHold();
                return;
            }

            locked = on;
            if (player != null)
            {
                if (on)
                {
                    controlLockedBefore = player.ControlLocked;
                    player.ControlLocked = true;
                }
                else
                {
                    player.ControlLocked = controlLockedBefore;
                }
            }

            if (swap != null)
            {
                if (on)
                {
                    swapEnabledBefore = swap.enabled;
                    swap.enabled = false;
                }
                else if (swapEnabledBefore)
                {
                    StartCoroutine(EnableSwapWhenReleased());
                }
            }

            UpdateHold();
        }

        // A click on Resume must not also swap, so the swap waits until the mouse button is up.
        private IEnumerator EnableSwapWhenReleased()
        {
            while (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                yield return null;
            }

            yield return null;
            if (!locked && swap != null)
            {
                swap.enabled = true;
            }
        }

        private void UpdateHold()
        {
            bool held = paused || failing || leaving || ending || arriving;
            GameplayHold.Active = held;
            TutorialGuide.HiddenByInterface = held;
        }

        //==================== POSITION LOST =====================
        // Called by RoomReset.Fail when a hazard is touched. Returns true when the return is played here.
        private bool OnHazard(RoomReset room)
        {
            if (failing)
            {
                return true;
            }

            if (paused || leaving || ending || !isActiveAndEnabled)
            {
                return false;
            }

            StartCoroutine(ReturnToRoom(room, true));
            return true;
        }

        private IEnumerator ReturnToRoom(RoomReset room, bool afterHazard)
        {
            failing = true;
            LockGameplay(true);
            float darken = afterHazard ? darkenSeconds : checkpointFadeSeconds;
            if (afterHazard)
            {
                // Contrast and colour drain out of the view as it goes dark.
                SetLook(-0.4f, -0.8f, -45f, darken, 1f);
                Play(positionLostClip);
            }

            SetState(afterHazard ? "Position lost" : "Restarting room");
            FadeTo(fader, 1f, darken);
            yield return WaitReal(darken);

            room.ResetRoom();
            CutCameras();

            if (afterHazard && holdSeconds > 0f)
            {
                ShowMessage(positionLostText, null, 0.25f, false);
                yield return WaitReal(holdSeconds);
                HideMessage(0.25f);
                yield return WaitReal(0.25f);
            }
            else
            {
                yield return WaitReal(0.1f);
            }

            float back = afterHazard ? returnSeconds : checkpointFadeSeconds;
            FadeTo(fader, 0f, back);
            SetLook(0f, 0f, 0f, back, 0f);
            failing = false;
            LockGameplay(false);
            SetState("Idle");
            if (logActions)
            {
                Debug.Log($"[{nameof(FacilityUI)}] {(afterHazard ? "Position lost" : "Restarted")}: back at the start of '{room.name}'.", this);
            }
        }

        // A reset by key or section key is instant; a brief dip to dark makes the jump readable.
        private void OnRoomReset()
        {
            if (failing || resetBlinkSeconds <= 0f)
            {
                return;
            }

            fader.style.display = DisplayStyle.Flex;
            fader.style.opacity = 0.85f;
            FadeTo(fader, 0f, resetBlinkSeconds);
        }

        // The gameplay camera cuts to the player instead of travelling across the level.
        private static void CutCameras()
        {
            foreach (CinemachineCamera camera in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            {
                camera.PreviousStateIsValid = false;
            }
        }

        private RoomReset CurrentRoom()
        {
            TrackRoom();
            if (lastRoom != null)
            {
                return lastRoom;
            }

            return player != null ? RoomReset.FindContaining(player.transform.position + Vector3.up) : null;
        }

        // Remembers the room the player is in, and saves the furthest one for Continue.
        private void TrackRoom()
        {
            if (session == null || player == null || arriving)
            {
                return;
            }

            IReadOnlyList<RoomReset> sections = session.Sections;
            for (int i = sections.Count - 1; i >= 0; i--)
            {
                if (sections[i] != null && sections[i].PlayerInside)
                {
                    lastRoom = sections[i];
                    if (recordProgress && !leaving)
                    {
                        GameProgress.Record(i);
                    }

                    return;
                }
            }
        }

        //==================== LOADING =====================
        /// <summary>Loads a scene behind the loading screen. Room puts the player at the start of that section (-1: the scene's own start).</summary>
        public void LoadScene(string scene, string message, int room)
        {
            if (leaving)
            {
                return;
            }

            StartCoroutine(Leave(scene, message, room));
        }

        private IEnumerator Leave(string scene, string message, int room)
        {
            leaving = true;
            LockGameplay(true);
            SetState($"Loading {scene}");
            SetLoadingVisible(message, 0f);
            loadingLayer.style.opacity = 0f;
            FadeTo(loadingLayer, 1f, 0.4f);
            yield return WaitReal(0.45f);

            CloseAllScreens();
            GameSettings.Save();
            Time.timeScale = 1f;
            paused = false;
            arrival = new Arrival { Message = message, Room = room };
            if (logActions)
            {
                Debug.Log($"[{nameof(FacilityUI)}] Loading '{scene}'.", this);
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(scene);
            if (operation == null)
            {
                Debug.LogError($"[{nameof(FacilityUI)}] Scene '{scene}' could not be loaded. Is it in Build Settings?", this);
                arrival = null;
                leaving = false;
                FadeTo(loadingLayer, 0f, 0.3f);
                LockGameplay(false);
                yield break;
            }

            // The line follows Unity's real loading progress, which reaches 0.9 when the scene is ready to switch.
            while (!operation.isDone)
            {
                SetLoadingProgress(operation.progress / 0.9f);
                yield return null;
            }
        }

        // In the new scene: puts the player at the chosen room, then takes the loading screen down.
        private IEnumerator Arrive()
        {
            if (arrival == null)
            {
                yield break;
            }

            Arrival here = arrival;
            LockGameplay(true);
            yield return null;
            if (here.Room >= 0 && session != null && here.Room < session.Sections.Count && session.Sections[here.Room] != null)
            {
                session.Sections[here.Room].ResetRoom();
                CutCameras();
            }

            // Held briefly at full, so the finished line can be read.
            yield return WaitReal(0.3f);
            arrival = null;
            arriving = false;
            LockGameplay(false);
            FadeTo(loadingLayer, 0f, 0.6f);
        }

        private void SetLoadingVisible(string message, float progress)
        {
            loadingLayer.style.display = DisplayStyle.Flex;
            loadingLayer.style.opacity = 1f;
            loadingMessage.text = message;
            SetLoadingProgress(progress);
        }

        private void SetLoadingProgress(float progress)
        {
            loadingProgress = Mathf.Clamp01(progress);
            loadingFill.style.width = Length.Percent(loadingProgress * 100f);
        }

        //==================== ENDING =====================
        /// <summary>Holds gameplay for the ending. The Ending Sequence drives what follows.</summary>
        public bool BeginEnding()
        {
            if (ending || leaving)
            {
                return false;
            }

            if (paused)
            {
                Resume();
            }

            ending = true;
            LockGameplay(true);
            SetState("Ending");
            return true;
        }

        /// <summary>Darkens and drains the view: 0 is the normal view, 1 fully dimmed.</summary>
        public void DimView(float amount, float seconds)
        {
            SetLook(-0.9f * amount, -1.3f * amount, -10f * amount, seconds, amount > 0f ? 1f : 0f);
        }

        /// <summary>Shows a line in the middle of the screen. Title style is the large, wide spacing of the ending.</summary>
        public void ShowMessage(string title, string subtitle, float seconds, bool titleStyle)
        {
            messageLayer.EnableInClassList("is-title", titleStyle);
            messageTitle.text = title;
            messageSubtitle.text = subtitle ?? string.Empty;
            messageSubtitle.style.opacity = string.IsNullOrEmpty(subtitle) ? 0f : 1f;
            FadeTo(messageLayer, 1f, seconds);
        }

        /// <summary>Fades in the second line under the message.</summary>
        public void ShowSubtitle(string subtitle, float seconds)
        {
            messageSubtitle.text = subtitle;
            messageSubtitle.style.opacity = 0f;
            FadeTo(messageSubtitle, 1f, seconds);
        }

        /// <summary>Starts the view from dark and fades it up.</summary>
        public void FadeFromDark(float seconds)
        {
            fader.style.display = DisplayStyle.Flex;
            fader.style.opacity = 1f;
            FadeTo(fader, 0f, seconds);
        }

        public void HideMessage(float seconds)
        {
            FadeTo(messageLayer, 0f, seconds);
        }

        //==================== SOUND =====================
        internal void PlayMove() => Play(moveClip);
        internal void PlayConfirm() => Play(confirmClip);
        internal void PlayBack() => Play(backClip);
        internal void PlayValue(bool up) => Play(up ? valueUpClip : valueDownClip);

        private void Play(AudioClip clip)
        {
            if (interfaceSource != null && clip != null)
            {
                interfaceSource.PlayOneShot(clip);
            }
        }

        //==================== DRAWING =====================
        private void BuildInterface()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }

            root.AddToClassList("fui-root");
            root.pickingMode = PickingMode.Ignore;

            screenLayer = Layer("Screens");
            fader = Layer("Fader");
            fader.AddToClassList("fui-fader");
            fader.style.opacity = 0f;
            fader.style.display = DisplayStyle.None;

            messageLayer = Layer("Message");
            messageLayer.AddToClassList("fui-message");
            messageTitle = new Label();
            messageTitle.AddToClassList("fui-message-title");
            messageSubtitle = new Label();
            messageSubtitle.AddToClassList("fui-message-subtitle");
            messageLayer.Add(messageTitle);
            messageLayer.Add(messageSubtitle);
            messageLayer.style.opacity = 0f;
            messageLayer.style.display = DisplayStyle.None;

            loadingLayer = Layer("Loading");
            loadingLayer.AddToClassList("fui-loading");
            var title = new Label("THE SPACE BETWEEN");
            title.AddToClassList("fui-loading-title");
            var bar = new VisualElement();
            bar.AddToClassList("fui-loading-bar");
            var track = new VisualElement();
            track.AddToClassList("fui-loading-track");
            loadingFill = new VisualElement();
            loadingFill.AddToClassList("fui-loading-fill");
            track.Add(loadingFill);
            loadingMessage = new Label();
            loadingMessage.AddToClassList("fui-loading-message");
            bar.Add(track);
            bar.Add(loadingMessage);
            loadingLayer.Add(title);
            loadingLayer.Add(bar);
            loadingLayer.style.display = DisplayStyle.None;
        }

        private VisualElement Layer(string layerName)
        {
            var layer = new VisualElement { name = layerName, pickingMode = PickingMode.Ignore };
            layer.AddToClassList("fui-layer");
            root.Add(layer);
            return layer;
        }

        // A global Color Adjustments override, on the layer the scene's own volume uses so the camera sees it.
        private void BuildLook()
        {
            var holder = new GameObject("Facility UI Look");
            holder.transform.SetParent(transform, false);
            Volume sceneVolume = FindAnyObjectByType<Volume>();
            if (sceneVolume != null)
            {
                holder.layer = sceneVolume.gameObject.layer;
            }

            lookProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            look = lookProfile.Add<ColorAdjustments>(true);
            look.colorFilter.overrideState = false;
            look.hueShift.overrideState = false;
            lookVolume = holder.AddComponent<Volume>();
            lookVolume.isGlobal = true;
            lookVolume.priority = 100f;
            lookVolume.weight = 0f;
            lookVolume.sharedProfile = lookProfile;
        }

        // Saturation and contrast are given from -1 to 0 and -100 to 100; exposure in stops.
        private void SetLook(float saturation, float exposure, float contrast, float seconds, float weight)
        {
            if (weight > 0f)
            {
                look.saturation.value = saturation * 100f;
                look.postExposure.value = exposure;
                look.contrast.value = contrast;
            }

            lookTarget = weight;
            lookSpeed = seconds > 0f ? 1f / seconds : float.PositiveInfinity;
        }

        private void TickLook()
        {
            if (lookVolume != null)
            {
                lookVolume.weight = Mathf.MoveTowards(lookVolume.weight, lookTarget, lookSpeed * Time.unscaledDeltaTime);
            }
        }

        private void FadeTo(VisualElement element, float target, float seconds)
        {
            fades.RemoveAll(f => f.Element == element);
            element.style.display = DisplayStyle.Flex;
            if (seconds <= 0f)
            {
                element.style.opacity = target;
                if (target <= 0f)
                {
                    element.style.display = DisplayStyle.None;
                }

                return;
            }

            fades.Add(new Fade { Element = element, Target = target, Speed = 1f / seconds });
        }

        private void TickFades()
        {
            for (int i = fades.Count - 1; i >= 0; i--)
            {
                Fade fade = fades[i];
                float now = Mathf.MoveTowards(fade.Element.style.opacity.value, fade.Target, fade.Speed * Time.unscaledDeltaTime);
                fade.Element.style.opacity = now;
                if (Mathf.Approximately(now, fade.Target))
                {
                    if (now <= 0f && fade.Element != messageSubtitle)
                    {
                        fade.Element.style.display = DisplayStyle.None;
                    }

                    fades.RemoveAt(i);
                }
            }
        }

        private static IEnumerator WaitReal(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until)
            {
                yield return null;
            }
        }

        private void SetState(string next)
        {
            state = next;
            if (logActions)
            {
                Debug.Log($"[{nameof(FacilityUI)}] {next}", this);
            }
        }
    }
}
