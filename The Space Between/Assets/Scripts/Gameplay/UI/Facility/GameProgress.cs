using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// How far the player has got: the furthest room (section) of the level reached, kept in PlayerPrefs. Continue on the
    /// main menu starts there; New Game clears it. The rooms are the Playtest Session's sections, in route order.
    /// </summary>
    public static class GameProgress
    {
        private const string RoomKey = "TheSpaceBetween.Progress.Room";

        public static bool HasSave => PlayerPrefs.HasKey(RoomKey);

        /// <summary>The furthest room reached, counted from 0, or -1 when there is no saved progress.</summary>
        public static int Room => PlayerPrefs.GetInt(RoomKey, -1);

        /// <summary>Saves a room if it is further than the one saved.</summary>
        public static void Record(int room)
        {
            if (room > Room)
            {
                PlayerPrefs.SetInt(RoomKey, room);
                PlayerPrefs.Save();
            }
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(RoomKey);
            PlayerPrefs.Save();
        }
    }
}
