using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace TheSpaceBetween
{
    /// <summary>
    /// Base class for everything that detects objects: it keeps the list of current detections
    /// and announces when something is detected or lost. Subclasses decide how detection works
    /// (raycast, trigger volume, field of view) and report results through AddSignal,
    /// UpdateSignalDistance, and RemoveSignal.
    ///
    /// Other systems read the results through Signals, TryGetNearest, and IsDetected, so they
    /// never depend on how a particular sensor finds its targets.
    /// </summary>
    public abstract class Sensor : MonoBehaviour
    {
        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked when a new object is detected. Passes the detected GameObject.")]
        [SerializeField] private UnityEvent<GameObject> detectedEvent;

        [Tooltip("Invoked when a detected object is lost. Passes the GameObject that was lost.")]
        [SerializeField] private UnityEvent<GameObject> lostEvent;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write a message to the Console whenever something is detected or lost.")]
        [SerializeField] private bool logDetections;

        [Tooltip("Number of objects currently detected.")]
        [ReadOnly, SerializeField] private int detectionCount;

        [Tooltip("Name of the closest detected object, or None.")]
        [ReadOnly, SerializeField] private string nearestTargetName = NoTargetLabel;

        private const string NoTargetLabel = "None";

        private readonly List<Signal> signals = new List<Signal>();

        //==================== OUTPUTS =====================
        /// <summary>Raised when a new object is detected.</summary>
        public event Action<Signal> SignalAdded;

        /// <summary>Raised when a detected object is lost.</summary>
        public event Action<Signal> SignalLost;

        /// <summary>All current detections. Do not modify from outside the sensor.</summary>
        public IReadOnlyList<Signal> Signals => signals;

        public bool HasDetections => signals.Count > 0;

        //==================== PUBLIC API =====================
        /// <summary>Finds the closest detection. Returns false when nothing is detected.</summary>
        public bool TryGetNearest(out Signal nearest)
        {
            nearest = default;

            if (signals.Count == 0)
            {
                return false;
            }

            nearest = signals[0];
            for (int i = 1; i < signals.Count; i++)
            {
                if (signals[i].Distance < nearest.Distance)
                {
                    nearest = signals[i];
                }
            }

            return true;
        }

        /// <summary>Returns true when the given object is currently detected.</summary>
        public bool IsDetected(GameObject target)
        {
            return IndexOf(target) >= 0;
        }

        //==================== FOR SUBCLASSES =====================
        /// <summary>Registers a detection. If the target is already known, only its distance is updated.</summary>
        protected void AddSignal(Signal signal)
        {
            if (signal.Target == null)
            {
                return;
            }

            int index = IndexOf(signal.Target);
            if (index >= 0)
            {
                signals[index] = signal;
                RefreshDebugFields();
                return;
            }

            signals.Add(signal);
            RefreshDebugFields();

            if (logDetections)
            {
                Debug.Log($"[{GetType().Name}] '{name}' detected '{signal.Target.name}' at {signal.Distance:F2}.", this);
            }

            SignalAdded?.Invoke(signal);
            detectedEvent?.Invoke(signal.Target);
        }

        /// <summary>Updates the distance of a target that is already detected.</summary>
        protected void UpdateSignalDistance(GameObject target, float distance)
        {
            int index = IndexOf(target);
            if (index < 0)
            {
                return;
            }

            Signal signal = signals[index];
            signal.Distance = distance;
            signals[index] = signal;
            RefreshDebugFields();
        }

        /// <summary>Removes a detection and announces that the target was lost.</summary>
        protected void RemoveSignal(GameObject target)
        {
            int index = IndexOf(target);
            if (index < 0)
            {
                return;
            }

            Signal lost = signals[index];
            signals.RemoveAt(index);
            RefreshDebugFields();

            if (logDetections)
            {
                string lostName = lost.Target != null ? lost.Target.name : "a destroyed object";
                Debug.Log($"[{GetType().Name}] '{name}' lost '{lostName}'.", this);
            }

            SignalLost?.Invoke(lost);
            lostEvent?.Invoke(lost.Target);
        }

        /// <summary>Removes every detection. Called automatically when the sensor is disabled.</summary>
        protected void ClearSignals()
        {
            for (int i = signals.Count - 1; i >= 0; i--)
            {
                Signal lost = signals[i];
                signals.RemoveAt(i);
                SignalLost?.Invoke(lost);
                lostEvent?.Invoke(lost.Target);
            }

            RefreshDebugFields();
        }

        protected virtual void OnDisable()
        {
            ClearSignals();
        }

        //==================== PRIVATE =====================
        // A destroyed GameObject compares equal to null, so IndexOf also matches entries whose
        // target was destroyed when the argument is null. Callers never pass null on purpose.
        private int IndexOf(GameObject target)
        {
            for (int i = 0; i < signals.Count; i++)
            {
                if (signals[i].Target == target)
                {
                    return i;
                }
            }

            return -1;
        }

        private void RefreshDebugFields()
        {
            detectionCount = signals.Count;
            nearestTargetName = TryGetNearest(out Signal nearest) && nearest.Target != null
                ? nearest.Target.name
                : NoTargetLabel;
        }
    }
}
