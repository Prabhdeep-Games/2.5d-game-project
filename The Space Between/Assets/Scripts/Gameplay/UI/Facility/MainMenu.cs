using UnityEngine;
using UnityEngine.UIElements;

namespace TheSpaceBetween
{
    /// <summary>
    /// The main menu: the title on the left and Continue, New Game, Settings, Credits and Quit below it, over a still
    /// view of the facility. Put it in the main menu scene next to a Facility UI with Allow Pause off.
    ///
    /// Continue is dimmed and skipped when there is no saved progress. Every option uses the Facility UI, so the menu
    /// and the pause menu share the same loading, settings and credits screens.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        //==================== MENU =====================
        [Header("Menu")]
        [Tooltip("The scene's Facility UI. Empty means the one in the scene.")]
        [SerializeField] private FacilityUI ui;

        [Tooltip("Seconds the view takes to come up from dark when the scene opens.")]
        [SerializeField, Min(0f)] private float fadeInSeconds = 2f;

        private MainMenuScreen screen;

        /// <summary>The option texts in order, for checks.</summary>
        public System.Collections.Generic.IReadOnlyList<string> Options => screen != null ? screen.Menu.Texts : null;

        public string SelectedOption => screen != null ? screen.Menu.SelectedText : null;

        public bool IsOptionEnabled(string option) => screen != null && screen.Menu.IsEnabled(option);

        /// <summary>Chooses an option as Enter or a click would. Returns false when it is missing or disabled.</summary>
        public bool Choose(string option) => screen != null && screen.Menu.Choose(option);

        private void Start()
        {
            if (ui == null)
            {
                ui = FindAnyObjectByType<FacilityUI>();
            }

            if (ui == null)
            {
                Debug.LogWarning($"[{nameof(MainMenu)}] No Facility UI in the scene, so there is no menu.", this);
                return;
            }

            screen = new MainMenuScreen(ui);
            ui.OpenScreen(screen);
            ui.FadeFromDark(fadeInSeconds);
        }

        private sealed class MainMenuScreen : FacilityScreen
        {
            public MainMenuScreen(FacilityUI ui) : base(ui, "Main Menu", "fui-main")
            {
                VisualElement block = Box("fui-main-block");
                block.Add(Text("THE SPACE BETWEEN", "fui-main-title"));
                block.Add(Box("fui-main-rule"));
                menu = new MenuList(ui, "fui-main-menu");
                Menu.Add("Continue", ui.ContinueGame, () => GameProgress.HasSave);
                Menu.Add("New Game", ui.NewGame);
                Menu.Add("Settings", ui.OpenSettings);
                Menu.Add("Credits", () => ui.OpenCredits());
                Menu.Add("Quit", ui.Quit);
                block.Add(Menu.Root);
                Root.Add(block);
            }

            private readonly MenuList menu;

            public override MenuList Menu => menu;

            public override void OnShow()
            {
                Menu.Reset();
            }

            public override void HandleInput(MenuInput input)
            {
                Menu.HandleInput(input);
            }
        }
    }
}
