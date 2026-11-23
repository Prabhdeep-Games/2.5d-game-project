namespace TheSpaceBetween
{
    /// <summary>A component with its own momentum. Swappable calls ResetMotion after a move that should not carry any.</summary>
    public interface IMotionResettable
    {
        void ResetMotion();
    }
}
