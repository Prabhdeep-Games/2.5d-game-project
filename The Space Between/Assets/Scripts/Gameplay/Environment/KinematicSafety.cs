using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Shared checks for scripted movers such as doors and platforms, which move their transform directly and
    /// are therefore not stopped by physics. They ask these checks before every step, and wait instead of moving
    /// into the player or a crate.
    ///
    /// Only bodies that can move count as obstructions: anything with a Character Controller, a dynamic
    /// Rigidbody or a Swappable. Static level geometry never stops a mover.
    /// </summary>
    public static class KinematicSafety
    {
        // Shrinks every overlap test a little, so bodies that only touch a surface do not count as overlapping it.
        private const float ContactTolerance = 0.03f;
        private const int BufferSize = 32;

        private static readonly Collider[] buffer = new Collider[BufferSize];

        /// <summary>
        /// Finds a movable body that the collider would overlap after moving by delta. Bodies in ignored are skipped
        /// (for example the riders the mover carries along). Returns null when the way is clear.
        /// </summary>
        public static Collider FindMovableInPath(Collider mover, Vector3 delta, ICollection<Transform> ignored = null)
        {
            if (mover == null || !mover.enabled)
            {
                return null;
            }

            Bounds bounds = mover.bounds;
            int count = OverlapShifted(bounds, delta);
            for (int i = 0; i < count; i++)
            {
                Collider other = buffer[i];
                if (other == mover || other.transform.IsChildOf(mover.transform))
                {
                    continue;
                }

                Transform owner = MovableOwner(other);
                if (owner != null && (ignored == null || !ignored.Contains(owner)))
                {
                    return other;
                }
            }

            return null;
        }

        /// <summary>
        /// True when a carried body moved by delta would overlap anything solid, apart from itself, the carrier and
        /// the other carried bodies. A platform uses it so it never lifts a rider into a ceiling.
        /// </summary>
        public static bool WouldBeCrushed(Transform rider, Vector3 delta, Collider carrier, ICollection<Transform> riders)
        {
            if (!TryGetSolidBounds(rider, out Bounds bounds))
            {
                return false;
            }

            // A Character Controller stops a skin width short of anything it moves toward, so it would be left
            // behind (and run over by the carrier) before its bounds touch the obstacle. Look that much further ahead.
            float margin = rider.TryGetComponent(out CharacterController controller) ? controller.skinWidth + ContactTolerance : 0f;
            Vector3 reach = delta.sqrMagnitude > 0f ? delta + delta.normalized * margin : delta;

            int count = OverlapShifted(bounds, reach);
            for (int i = 0; i < count; i++)
            {
                Collider other = buffer[i];
                if (other == carrier || other.transform.IsChildOf(rider))
                {
                    continue;
                }

                Transform owner = MovableOwner(other);
                if (owner != null && riders != null && riders.Contains(owner))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// The transform that moves the body this collider belongs to: the Character Controller, dynamic Rigidbody
        /// or Swappable that owns it. Returns null for static geometry.
        /// </summary>
        public static Transform MovableOwner(Collider collider)
        {
            if (collider is CharacterController)
            {
                return collider.transform;
            }

            Rigidbody body = collider.attachedRigidbody;
            if (body != null && !body.isKinematic)
            {
                return body.transform;
            }

            Swappable swappable = collider.GetComponentInParent<Swappable>();
            return swappable != null ? swappable.transform : null;
        }

        /// <summary>The combined bounds of the solid colliders on an object and its children.</summary>
        public static bool TryGetSolidBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            Collider[] colliders = root.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i].isTrigger || !colliders[i].enabled)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = colliders[i].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(colliders[i].bounds);
                }
            }

            return found;
        }

        private static int OverlapShifted(Bounds bounds, Vector3 delta)
        {
            Vector3 halfExtents = bounds.extents - Vector3.one * ContactTolerance;
            halfExtents = Vector3.Max(halfExtents, Vector3.zero);
            return Physics.OverlapBoxNonAlloc(bounds.center + delta, halfExtents, buffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        }
    }
}
