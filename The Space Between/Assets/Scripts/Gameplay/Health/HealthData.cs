using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Configuration for a Health component. Create one asset per kind of entity
    /// (for example one for the player and one for a fragile object) and share it between
    /// every instance of that kind.
    /// </summary>
    [CreateAssetMenu(fileName = "NewHealthData", menuName = "The Space Between/Data/Health Data")]
    public class HealthData : ScriptableObject
    {
        //==================== STATS =====================
        [Header("Stats")]
        [Tooltip("Maximum health points.")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;

        [Tooltip("Minimum seconds between two accepted hits. Zero means every hit counts.")]
        [SerializeField, Min(0f)] private float damageCooldown;

        //==================== PUBLIC API =====================
        public float MaxHealth => maxHealth;

        public float DamageCooldown => damageCooldown;
    }
}
