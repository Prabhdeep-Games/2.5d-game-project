namespace TheSpaceBetween
{
    /// <summary>How a SwapAbility decides which object it is aiming at.</summary>
    public enum SwapAimMode
    {
        /// <summary>The mouse cursor or the right stick sets a free aim direction.</summary>
        Pointer,

        /// <summary>The aim follows the way the player last moved, left or right.</summary>
        Facing,

        /// <summary>Old behaviour: the nearest valid object in any direction. Kept for comparison and tests.</summary>
        Nearest
    }
}
