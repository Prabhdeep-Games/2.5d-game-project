using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>The volume groups the settings control. Each AudioSource joins one with an AudioChannel.</summary>
    public enum AudioChannelKind
    {
        Effects,
        Ambience,
        Music
    }

    /// <summary>
    /// The player's display and audio settings, kept in PlayerPrefs and applied to Unity's own systems: the screen,
    /// the quality level, VSync, the AudioListener (master) and the AudioChannel groups. Nothing here is shown on screen;
    /// the Settings screen reads and changes these values.
    /// </summary>
    public static class GameSettings
    {
        private const string Prefix = "TheSpaceBetween.Settings.";

        private static bool loaded;
        private static int width;
        private static int height;
        private static bool fullscreen;
        private static bool vSync;
        private static int quality;
        private static float master;
        private static float effects;
        private static float ambience;
        private static float music;

        public static int Width { get { Load(); return width; } }
        public static int Height { get { Load(); return height; } }

        public static bool Fullscreen
        {
            get { Load(); return fullscreen; }
            set { Load(); fullscreen = value; ApplyDisplay(); }
        }

        public static bool VSync
        {
            get { Load(); return vSync; }
            set { Load(); vSync = value; ApplyFrameTiming(); }
        }

        public static int Quality
        {
            get { Load(); return quality; }
            set { Load(); quality = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1); ApplyQuality(); }
        }

        public static float Master
        {
            get { Load(); return master; }
            set { Load(); master = Mathf.Clamp01(value); AudioListener.volume = master; }
        }

        /// <summary>The volume of one group, from 0 to 1.</summary>
        public static float Volume(AudioChannelKind kind)
        {
            Load();
            switch (kind)
            {
                case AudioChannelKind.Ambience: return ambience;
                case AudioChannelKind.Music: return music;
                default: return effects;
            }
        }

        public static void SetVolume(AudioChannelKind kind, float value)
        {
            Load();
            value = Mathf.Clamp01(value);
            switch (kind)
            {
                case AudioChannelKind.Ambience: ambience = value; break;
                case AudioChannelKind.Music: music = value; break;
                default: effects = value; break;
            }

            AudioChannel.RefreshAll();
        }

        public static void SetResolution(int newWidth, int newHeight)
        {
            Load();
            width = newWidth;
            height = newHeight;
            ApplyDisplay();
        }

        /// <summary>Applies every setting. The Facility UI calls it once a scene has started.</summary>
        public static void ApplyAll()
        {
            Load();
            ApplyQuality();
            ApplyDisplay();
            AudioListener.volume = master;
            AudioChannel.RefreshAll();
        }

        public static void Save()
        {
            Load();
            PlayerPrefs.SetInt(Prefix + "Width", width);
            PlayerPrefs.SetInt(Prefix + "Height", height);
            PlayerPrefs.SetInt(Prefix + "Fullscreen", fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "VSync", vSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Quality", quality);
            PlayerPrefs.SetFloat(Prefix + "Master", master);
            PlayerPrefs.SetFloat(Prefix + "Effects", effects);
            PlayerPrefs.SetFloat(Prefix + "Ambience", ambience);
            PlayerPrefs.SetFloat(Prefix + "Music", music);
            PlayerPrefs.Save();
        }

        // Defaults are what the game does today: the current screen and quality, and VSync off, because the playtest
        // runs at a capped 60 frames per second on every computer.
        private static void Load()
        {
            if (loaded)
            {
                return;
            }

            loaded = true;
            Resolution current = Screen.currentResolution;
            width = PlayerPrefs.GetInt(Prefix + "Width", Screen.fullScreen ? current.width : Screen.width);
            height = PlayerPrefs.GetInt(Prefix + "Height", Screen.fullScreen ? current.height : Screen.height);
            fullscreen = PlayerPrefs.GetInt(Prefix + "Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
            vSync = PlayerPrefs.GetInt(Prefix + "VSync", 0) == 1;
            quality = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Quality", QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1);
            master = PlayerPrefs.GetFloat(Prefix + "Master", 1f);
            effects = PlayerPrefs.GetFloat(Prefix + "Effects", 1f);
            ambience = PlayerPrefs.GetFloat(Prefix + "Ambience", 0.7f);
            music = PlayerPrefs.GetFloat(Prefix + "Music", 0.6f);
        }

        // The Editor's Game view has no real screen mode, so the screen is only changed in a build.
        private static void ApplyDisplay()
        {
            if (Application.isEditor)
            {
                return;
            }

            Screen.SetResolution(width, height, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        // Changing the quality level resets VSync, so the frame timing is applied after it.
        private static void ApplyQuality()
        {
            if (QualitySettings.GetQualityLevel() != quality)
            {
                QualitySettings.SetQualityLevel(quality, true);
            }

            ApplyFrameTiming();
        }

        // With VSync on, Unity ignores the playtest's frame rate cap and follows the monitor instead.
        private static void ApplyFrameTiming()
        {
            QualitySettings.vSyncCount = vSync ? 1 : 0;
        }
    }
}
