using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Small facts about what the player has seen, kept with the saved progress, so a later level can quietly reflect
    /// them. Today there is one: "SawOccupiedCell", set by looking into the one lit cell in the Containment Wing; the
    /// exterior then shows a single lit window far up the facility. New Game clears them.
    /// </summary>
    public static class NarrativeFlags
    {
        public const string SawOccupiedCell = "SawOccupiedCell";

        private const string Key = "TheSpaceBetween.Progress.Flags";

        public static bool Get(string flag)
        {
            return ("," + PlayerPrefs.GetString(Key, string.Empty) + ",").Contains("," + flag + ",");
        }

        public static void Set(string flag)
        {
            if (Get(flag))
            {
                return;
            }

            string current = PlayerPrefs.GetString(Key, string.Empty);
            PlayerPrefs.SetString(Key, string.IsNullOrEmpty(current) ? flag : current + "," + flag);
            PlayerPrefs.Save();
        }

        public static void ClearAll()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
