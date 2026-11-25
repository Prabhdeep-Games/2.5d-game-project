namespace TheSpaceBetween
{
    /// <summary>
    /// Whether gameplay is on hold because the interface has taken over: the pause menu, the "position lost" return
    /// or the ending. While it is on, gameplay keys such as restart, use and observe do nothing, so a key pressed in a
    /// menu never also acts in the level. Set only by the Facility UI.
    /// </summary>
    public static class GameplayHold
    {
        public static bool Active { get; internal set; }
    }
}
