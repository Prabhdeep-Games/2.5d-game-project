namespace TheSpaceBetween
{
    /// <summary>
    /// Anything that can receive damage. Hazards and attacks depend on this interface instead of
    /// a concrete health component, so they work on the player, enemies, and breakable objects alike.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>True once the object has run out of health.</summary>
        bool IsDead { get; }

        /// <summary>Applies damage. Implementations decide whether the hit is accepted.</summary>
        void TakeDamage(float amount);
    }
}
