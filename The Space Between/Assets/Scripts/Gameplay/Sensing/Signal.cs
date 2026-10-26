using System;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>The result of one detection: what was detected and how far away it is.</summary>
    [Serializable]
    public struct Signal
    {
        /// <summary>The detected GameObject.</summary>
        public GameObject Target;

        /// <summary>Distance from the sensor to the target in world units.</summary>
        public float Distance;
    }
}
