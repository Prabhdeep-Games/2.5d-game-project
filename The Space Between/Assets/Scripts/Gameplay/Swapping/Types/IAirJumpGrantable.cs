namespace TheSpaceBetween
{
    /// <summary>A mover that can be given one extra jump in mid-air. A swap that leaves the player airborne grants it.</summary>
    public interface IAirJumpGrantable
    {
        void GrantAirJump();
    }
}
