using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Base class for every part of the building that can be switched: sealed doors, movable platforms and
    /// security cameras. It answers one question for all of them: is this mechanism working right now?
    ///
    /// Two things decide it:
    ///   Power:    set by a control panel (or a script). Unpowered mechanisms rest in their starting state.
    ///   Lockdown: set by a security camera while it sees the player. A locked down mechanism behaves as if
    ///             unpowered, whatever its power says, and recovers when every camera has released it.
    /// The mechanism is Active when it is powered and not locked down. Subclasses react in OnActiveChanged.
    ///
    /// The optional indicator light shows the state with the same colours everywhere in the game:
    /// green active, red unpowered, amber locked down.
    /// </summary>
    public abstract class Mechanism : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        //==================== INDICATOR =====================
        [Header("Indicator")]
        [Tooltip("Optional light that shows the state. Its colour is set with a MaterialPropertyBlock, so the material asset is never changed.")]
        [SerializeField] private Renderer indicator;

        [Tooltip("Indicator colour while the mechanism is active.")]
        [SerializeField] private Color activeColor = new Color(0.3f, 1f, 0.5f);

        [Tooltip("Indicator colour while the mechanism is unpowered.")]
        [SerializeField] private Color unpoweredColor = new Color(1f, 0.3f, 0.25f);

        [Tooltip("Indicator colour while a security camera holds the mechanism in lockdown.")]
        [SerializeField] private Color lockdownColor = new Color(1f, 0.7f, 0.1f);

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write power and lockdown changes to the Console.")]
        [SerializeField] private bool logChanges;

        [Tooltip("Whether the mechanism is powered right now.")]
        [ReadOnly, SerializeField] private bool isPowered;

        [Tooltip("Whether at least one security camera holds the mechanism in lockdown.")]
        [ReadOnly, SerializeField] private bool isLockedDown;

        [Tooltip("Powered and not locked down: the mechanism is doing its job.")]
        [ReadOnly, SerializeField] private bool isActive;

        private readonly List<UnityEngine.Object> lockdownSources = new List<UnityEngine.Object>();
        private MaterialPropertyBlock block;

        //==================== OUTPUTS =====================
        /// <summary>Raised when Active changes. Passes the new value.</summary>
        public event Action<bool> ActiveChanged;

        public bool IsPowered => isPowered;

        public bool IsLockedDown => isLockedDown;

        /// <summary>Powered and not locked down.</summary>
        public bool IsActive => isActive;

        //==================== PUBLIC API =====================
        /// <summary>Powers the mechanism on or off.</summary>
        public void SetPowered(bool powered)
        {
            if (isPowered == powered)
            {
                return;
            }

            isPowered = powered;
            Log(powered ? "powered" : "unpowered");
            Refresh();
        }

        /// <summary>
        /// Adds or removes one source of lockdown. Each camera passes itself, so two cameras watching the same
        /// door both have to release it before it works again.
        /// </summary>
        public void SetLockdown(UnityEngine.Object source, bool locked)
        {
            if (source == null)
            {
                return;
            }

            bool known = lockdownSources.Contains(source);
            if (locked && !known)
            {
                lockdownSources.Add(source);
            }
            else if (!locked && known)
            {
                lockdownSources.Remove(source);
            }
            else
            {
                return;
            }

            isLockedDown = lockdownSources.Count > 0;
            Log(isLockedDown ? $"locked down by '{source.name}'" : "released from lockdown");
            Refresh();
        }

        //==================== FOR SUBCLASSES =====================
        /// <summary>The power the mechanism starts with. Each subclass shows its own setting for it.</summary>
        protected abstract bool StartsPowered { get; }

        /// <summary>
        /// Called when Active changes after the scene has started. The starting state is applied silently,
        /// so a door that starts unlocked does not open by itself.
        /// </summary>
        protected abstract void OnActiveChanged(bool active);

        protected virtual void Awake()
        {
            isPowered = StartsPowered;
            isLockedDown = false;
            isActive = isPowered;
            block = new MaterialPropertyBlock();
            ShowState();
        }

        protected virtual void OnDisable()
        {
            lockdownSources.Clear();
            isLockedDown = false;
        }

        protected void Log(string message)
        {
            if (logChanges)
            {
                Debug.Log($"[{GetType().Name}] '{name}' {message}.", this);
            }
        }

        //==================== PRIVATE =====================
        private void Refresh()
        {
            bool active = isPowered && !isLockedDown;
            ShowState();

            if (active == isActive)
            {
                return;
            }

            isActive = active;
            OnActiveChanged(active);
            ActiveChanged?.Invoke(active);
        }

        private void ShowState()
        {
            if (indicator == null || indicator.sharedMaterial == null)
            {
                return;
            }

            Color color = isLockedDown ? lockdownColor : isPowered ? activeColor : unpoweredColor;
            int colorId = indicator.sharedMaterial.HasProperty(BaseColorId) ? BaseColorId : LegacyColorId;
            indicator.GetPropertyBlock(block);
            block.SetColor(colorId, color);
            indicator.SetPropertyBlock(block);
        }
    }
}
