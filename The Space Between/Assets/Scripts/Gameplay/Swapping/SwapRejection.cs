namespace TheSpaceBetween
{
    /// <summary>Why a swap did not happen. None means the swap succeeded.</summary>
    public enum SwapRejection
    {
        None,
        NoTargetInRange,
        TargetLocked,
        LineOfSightBlocked,
        DestinationBlocked
    }

    public static class SwapRejectionText
    {
        /// <summary>A short sentence a player or a tester can read to understand the rejection.</summary>
        public static string Describe(SwapRejection reason)
        {
            switch (reason)
            {
                case SwapRejection.None:
                    return "Swap succeeded.";
                case SwapRejection.NoTargetInRange:
                    return "Nothing to swap with is within range.";
                case SwapRejection.TargetLocked:
                    return "That object cannot be swapped right now.";
                case SwapRejection.LineOfSightBlocked:
                    return "Something solid is blocking the view of the target.";
                case SwapRejection.DestinationBlocked:
                    return "There is not enough room to fit at the new position.";
                default:
                    return reason.ToString();
            }
        }
    }
}
