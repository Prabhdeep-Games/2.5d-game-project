using System;
using UnityEngine;
using UnityEngine.Events;

namespace TheSpaceBetween
{
    /// <summary>
    /// Tracks the current health of one entity and applies damage and healing.
    ///
    /// This component only reports what happened. It never destroys the object or loads a scene.
    /// What death means (respawn, restart, animation) belongs to other systems that listen to Died.
    /// </summary>
    public class Health : MonoBehaviour, IDamageable
    {
        //==================== CONFIGURATION =====================
        [Header("Configuration")]
        [Tooltip("Data asset with the maximum health and the damage cooldown. Create one with Assets > Create > The Space Between > Data > Health Data.")]
        [SerializeField] private HealthData data;

        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked after damage was applied. Passes the amount of damage.")]
        [SerializeField] private UnityEvent<float> damagedEvent;

        [Tooltip("Invoked after healing was applied. Passes the amount healed.")]
        [SerializeField] private UnityEvent<float> healedEvent;

        [Tooltip("Invoked once when health reaches zero.")]
        [SerializeField] private UnityEvent diedEvent;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write damage, healing, and death messages to the Console.")]
        [SerializeField] private bool logChanges;

        [Tooltip("Current health during play.")]
        [ReadOnly, SerializeField] private float currentHealth;

        [Tooltip("True once health has reached zero.")]
        [ReadOnly, SerializeField] private bool isDead;

        private float lastDamageTime = float.NegativeInfinity;

        //==================== OUTPUTS =====================
        /// <summary>Raised after damage was applied. Passes the amount of damage.</summary>
        public event Action<float> Damaged;

        /// <summary>Raised after healing was applied. Passes the amount healed.</summary>
        public event Action<float> Healed;

        /// <summary>Raised once when health reaches zero.</summary>
        public event Action Died;

        public float CurrentHealth => currentHealth;

        public float MaxHealth => data != null ? data.MaxHealth : 0f;

        /// <summary>Current health divided by maximum health, from 0 to 1.</summary>
        public float HealthRatio => MaxHealth > 0f ? currentHealth / MaxHealth : 0f;

        public bool IsDead => isDead;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            if (data == null)
            {
                Debug.LogError($"{nameof(Health)} on '{name}' has no {nameof(HealthData)} assigned, so it is disabled.", this);
                enabled = false;
                return;
            }

            currentHealth = data.MaxHealth;
        }

        //==================== PUBLIC API =====================
        /// <summary>Applies damage. Ignored when dead, not positive, or still inside the damage cooldown.</summary>
        public void TakeDamage(float amount)
        {
            if (data == null || isDead || amount <= 0f)
            {
                return;
            }

            if (Time.time - lastDamageTime < data.DamageCooldown)
            {
                return;
            }

            lastDamageTime = Time.time;
            currentHealth = Mathf.Max(0f, currentHealth - amount);

            if (logChanges)
            {
                Debug.Log($"[Health] '{name}' took {amount} damage, {currentHealth}/{MaxHealth} left.", this);
            }

            Damaged?.Invoke(amount);
            damagedEvent?.Invoke(amount);

            if (currentHealth <= 0f)
            {
                Die();
            }
        }

        /// <summary>Restores health up to the maximum. Ignored when dead or not positive.</summary>
        public void Heal(float amount)
        {
            if (data == null || isDead || amount <= 0f)
            {
                return;
            }

            float before = currentHealth;
            currentHealth = Mathf.Min(data.MaxHealth, currentHealth + amount);
            float healed = currentHealth - before;

            if (logChanges)
            {
                Debug.Log($"[Health] '{name}' healed {healed}, {currentHealth}/{MaxHealth} now.", this);
            }

            Healed?.Invoke(healed);
            healedEvent?.Invoke(healed);
        }

        /// <summary>Brings a dead or damaged entity back to full health, for example at a checkpoint.</summary>
        public void Revive()
        {
            if (data == null)
            {
                return;
            }

            isDead = false;
            currentHealth = data.MaxHealth;
            lastDamageTime = float.NegativeInfinity;
        }

        //==================== PRIVATE =====================
        private void Die()
        {
            isDead = true;

            if (logChanges)
            {
                Debug.Log($"[Health] '{name}' died.", this);
            }

            Died?.Invoke();
            diedEvent?.Invoke();
        }

        //==================== DEBUG ACTIONS =====================
        // These appear in the component's right-click menu in the Inspector.
        [ContextMenu("Debug: Take 10 Damage")]
        private void DebugTakeDamage() => TakeDamage(10f);

        [ContextMenu("Debug: Heal 10")]
        private void DebugHeal() => Heal(10f);

        [ContextMenu("Debug: Kill")]
        private void DebugKill() => TakeDamage(currentHealth);
    }
}
