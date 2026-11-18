using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// Keeps the Editor from stopping to ask about saving:
    ///   - Open scenes with unsaved changes are saved just before Play Mode starts, and every Interval seconds while
    ///     editing, so switching scenes does not bring up "Scene(s) Have Been Modified".
    ///   - Cinemachine's "Save During Play" stays off, so camera values that scripts change while playing (such as the
    ///     wall run lookahead from CameraWallRunFraming) are never copied back into the scene, and its "Save changes made
    ///     in Play Mode" question never appears.
    /// Untitled scenes are left alone, since saving them would need a file dialog. Undo still works after an auto save.
    /// Turn it on or off with Tools > Auto Save Scenes.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoSaveScenes
    {
        private const string MenuPath = "Tools/Auto Save Scenes";
        private const string EnabledKey = "TheSpaceBetween.AutoSaveScenes";
        private const string CinemachineSaveDuringPlayKey = "SaveDuringPlay_Enabled"; // Unity.Cinemachine.Editor.SaveDuringPlay
        private const double Interval = 60.0;

        private static double nextSave;

        static AutoSaveScenes()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnUpdate;
            EditorApplication.delayCall += ApplyCinemachineSetting;
            nextSave = EditorApplication.timeSinceStartup + Interval;
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, true);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            ApplyCinemachineSetting();
            Debug.Log($"[{nameof(AutoSaveScenes)}] Auto save is {(Enabled ? "on" : "off")}.");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void ApplyCinemachineSetting()
        {
            if (Enabled && EditorPrefs.GetBool(CinemachineSaveDuringPlayKey, false))
            {
                EditorPrefs.SetBool(CinemachineSaveDuringPlayKey, false);
                Debug.Log($"[{nameof(AutoSaveScenes)}] Turned off Cinemachine Save During Play, so play-mode camera values stay out of the scenes.");
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                ApplyCinemachineSetting();
                SaveDirtyScenes("before Play Mode");
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                nextSave = EditorApplication.timeSinceStartup + Interval;
            }
        }

        private static void OnUpdate()
        {
            if (EditorApplication.timeSinceStartup < nextSave)
            {
                return;
            }

            nextSave = EditorApplication.timeSinceStartup + Interval;
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                SaveDirtyScenes(null);
            }
        }

        private static void SaveDirtyScenes(string reason)
        {
            if (!Enabled)
            {
                return;
            }

            int saved = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.isDirty && !string.IsNullOrEmpty(scene.path) && EditorSceneManager.SaveScene(scene))
                {
                    saved++;
                }
            }

            if (saved > 0 && reason != null)
            {
                Debug.Log($"[{nameof(AutoSaveScenes)}] Saved {saved} scene(s) {reason}.");
            }
        }
    }
}
