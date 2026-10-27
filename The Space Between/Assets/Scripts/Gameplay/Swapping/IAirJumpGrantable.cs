namespace TheSpaceBetween
{
    /// <summary>
    /// Implemented by a movement component that can be given one extra jump to use in mid-air.
    /// A swap that leaves the player airborne grants it. Landing removes any unused extra jump.
    /// </summary>
    public interface IAirJumpGrantable
    {
        void GrantAirJump();
    }
}
