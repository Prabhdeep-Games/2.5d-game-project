using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>Implemented by a mover that knows which way it is facing, so aiming can follow it.</summary>
    public interface IFacingSource
    {
        /// <summary>A unit vector in the world pointing the way the object faces.</summary>
        Vector3 Facing { get; }
    }
}
