namespace TheSpaceBetween
{
    /// <summary>How an accepted swap plays out.</summary>
    public enum SwapMotion
    {
        /// <summary>Game time stops, both bodies shrink, trade places while tiny, grow back and keep their momentum.</summary>
        Shrink,

        /// <summary>Both bodies trade places in a single frame and lose their momentum.</summary>
        Instant
    }
}
