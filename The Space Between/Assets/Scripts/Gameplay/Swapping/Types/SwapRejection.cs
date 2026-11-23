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
}
