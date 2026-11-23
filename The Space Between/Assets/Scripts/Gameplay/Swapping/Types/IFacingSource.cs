using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>A mover that knows which way it faces, so Facing aim can follow it.</summary>
    public interface IFacingSource
    {
        /// <summary>A unit vector in the world pointing the way the object faces.</summary>
        Vector3 Facing { get; }
    }
}
