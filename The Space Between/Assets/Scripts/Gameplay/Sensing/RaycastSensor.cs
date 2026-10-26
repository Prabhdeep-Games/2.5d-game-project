using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Detects the first object hit by a ray or sphere cast every frame. Because only the first
    /// hit counts, anything solid on the detection layers blocks the view of what is behind it.
    /// That makes this sensor a direct way to test line of sight.
    ///
    /// Uses 3D physics, which matches a 2.5D game built with 3D colliders.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaycastSensor : Sensor
    {
        //==================== CONFIGURATION =====================
        [Header("Detection")]
        [Tooltip("Direction of the cast in the local space of this transform. The default points along the local X axis, which suits a side scroller.")]
        [SerializeField] private Vector3 localDirection = Vector3.right;

        [Tooltip("Maximum length of the cast in world units, measured from this transform.")]
        [SerializeField, Min(0.01f)] private float maxDistance = 10f;

        [Tooltip("Radius of the cast in world units. Zero casts a thin ray.")]
        [SerializeField, Min(0f)] private float castRadius;

        [Tooltip("Layers the cast can hit. Anything solid on these layers can block the view of objects behind it.")]
        [SerializeField] private LayerMask detectionLayers = ~0;

        [Tooltip("Whether trigger colliders count as hits.")]
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("World position where the cast last hit something.")]
        [ReadOnly, SerializeField] private Vector3 lastHitPoint;

        // A separate flag is used instead of a null check because a destroyed GameObject
        // compares equal to null, and a destroyed target must still be removed from the list.
        private GameObject currentTarget;
        private bool hasTarget;

        public bool IsHitting => hasTarget;

        public Vector3 LastHitPoint => lastHitPoint;

        //==================== LIFECYCLE =====================
        private void Update()
        {
            if (TryCast(out RaycastHit hit))
            {
                HandleHit(hit);
            }
            else
            {
                HandleNoHit();
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            currentTarget = null;
            hasTarget = false;
        }

        private void OnValidate()
        {
            if (localDirection.sqrMagnitude < 0.0001f)
            {
                Debug.LogWarning($"{nameof(RaycastSensor)} on '{name}' had a zero direction. Reset to the local X axis.", this);
                localDirection = Vector3.right;
            }
        }

        //==================== PRIVATE =====================
        private Vector3 WorldDirection => transform.TransformDirection(localDirection.normalized);

        private bool TryCast(out RaycastHit hit)
        {
            Vector3 origin = transform.position;
            Vector3 direction = WorldDirection;

            if (castRadius > 0f)
            {
                return Physics.SphereCast(origin, castRadius, direction, out hit, maxDistance, detectionLayers, triggerInteraction);
            }

            return Physics.Raycast(origin, direction, out hit, maxDistance, detectionLayers, triggerInteraction);
        }

        private void HandleHit(RaycastHit hit)
        {
            GameObject target = hit.collider.gameObject;
            lastHitPoint = hit.point;

            if (hasTarget && target == currentTarget)
            {
                UpdateSignalDistance(target, hit.distance);
                return;
            }

            if (hasTarget)
            {
                RemoveSignal(currentTarget);
            }

            currentTarget = target;
            hasTarget = true;
            AddSignal(new Signal { Target = target, Distance = hit.distance });
        }

        private void HandleNoHit()
        {
            if (!hasTarget)
            {
                return;
            }

            RemoveSignal(currentTarget);
            currentTarget = null;
            hasTarget = false;
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            Vector3 origin = transform.position;
            Vector3 end = origin + WorldDirection * maxDistance;

            Gizmos.color = IsHitting ? Color.green : Color.red;
            Gizmos.DrawLine(origin, end);

            if (castRadius > 0f)
            {
                Gizmos.DrawWireSphere(origin, castRadius);
                Gizmos.DrawWireSphere(end, castRadius);
            }
        }
    }
}
