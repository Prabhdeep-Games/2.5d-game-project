namespace TheSpaceBetween
{
    /// <summary>
    /// Implemented by components that keep their own momentum (for example a character controller
    /// with a vertical speed). Swappable calls ResetMotion after moving, so no momentum carries over.
    /// </summary>
    public interface IMotionResettable
    {
        void ResetMotion();
    }
}
