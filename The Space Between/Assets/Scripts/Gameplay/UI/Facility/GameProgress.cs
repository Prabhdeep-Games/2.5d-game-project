using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// How far the player has got: the furthest level and room (section) reached, kept in PlayerPrefs. Continue on the
    /// main menu starts there; New Game clears it. Levels are ordered by the Facility UI's Level Order; the rooms are
    /// each level's Playtest Session sections, in route order.
    /// </summary>
    public static class GameProgress
    {
        private const string SceneKey = "TheSpaceBetween.Progress.Scene";
        private const string LevelKey = "TheSpaceBetween.Progress.Level";
        private const string RoomKey = "TheSpaceBetween.Progress.Room";

        public static bool HasSave => PlayerPrefs.HasKey(RoomKey) && !string.IsNullOrEmpty(Scene);

        /// <summary>The scene of the saved progress, or empty.</summary>
        public static string Scene => PlayerPrefs.GetString(SceneKey, string.Empty);

        /// <summary>Where the saved scene sits in the level order (-1 for a scene outside it), or -2 with no save.</summary>
        public static int Level => HasSave ? PlayerPrefs.GetInt(LevelKey, -1) : -2;

        /// <summary>The furthest room reached in the saved scene, counted from 0, or -1 when there is no saved progress.</summary>
        public static int Room => HasSave ? PlayerPrefs.GetInt(RoomKey, -1) : -1;

        /// <summary>Saves a room if it is further on than the one saved: a later level, or a later room of the same one.</summary>
        public static void Record(string scene, int level, int room)
        {
            bool further = !HasSave || level > Level || (level == Level && scene == Scene && room > Room);
            if (!further)
            {
                return;
            }

            PlayerPrefs.SetString(SceneKey, scene);
            PlayerPrefs.SetInt(LevelKey, level);
            PlayerPrefs.SetInt(RoomKey, room);
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(SceneKey);
            PlayerPrefs.DeleteKey(LevelKey);
            PlayerPrefs.DeleteKey(RoomKey);
            PlayerPrefs.Save();
        }
    }
}
