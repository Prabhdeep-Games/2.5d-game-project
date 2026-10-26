using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// One behaviour an entity can be in, such as Monitoring, Detecting, or Resetting.
    /// Add a State subclass to the same GameObject as a StateMachine and place it in that
    /// machine's priority list.
    ///
    /// Do not use Unity's Update in a State subclass. Per-frame logic belongs in Tick, which the
    /// StateMachine calls only while this state is the active one.
    /// </summary>
    public abstract class State : MonoBehaviour
    {
        /// <summary>
        /// Returns true when this state should be active given the current situation.
        /// The StateMachine asks every state in priority order each frame, including states that
        /// are not active, so this method must be cheap and must not change anything.
        /// </summary>
        public abstract bool CanRun();

        /// <summary>Called once when this state becomes the active state.</summary>
        public virtual void OnEnter()
        {
        }

        /// <summary>Called every frame while this state is the active state.</summary>
        public virtual void Tick()
        {
        }

        /// <summary>Called once when this state stops being the active state.</summary>
        public virtual void OnExit()
        {
        }
    }
}
