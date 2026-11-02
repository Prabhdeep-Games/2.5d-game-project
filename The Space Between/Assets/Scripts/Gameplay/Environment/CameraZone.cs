using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// A volume that pulls the camera further back while the player is inside it. Used in vertical shafts, so the
    /// whole height is visible and the player looks small against it: scale and vulnerability, and a readable
    /// route upward.
    ///
    /// The volume is the Box Collider on this object (a trigger on the Ignore Raycast layer). The camera's
    /// CameraWallRunFraming reads ExtraDistanceAt and blends toward it, so it stays the only script that sets the
    /// camera distance. Where zones overlap, the largest extra distance wins.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class CameraZone : MonoBehaviour
    {
        private static readonly List<CameraZone> zones = new List<CameraZone>();

        //==================== FRAMING =====================
        [Header("Framing")]
        [Tooltip("How much further back the camera sits while the player is inside, in world units.")]
        [SerializeField, Min(0f)] private float extraDistance = 6f;

        private BoxCollider area;

        public float ExtraDistance => extraDistance;

        /// <summary>The extra camera distance for a player at this point: the largest of the zones containing it, or 0.</summary>
        public static float ExtraDistanceAt(Vector3 point)
        {
            float extra = 0f;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].area.bounds.Contains(point))
                {
                    extra = Mathf.Max(extra, zones[i].extraDistance);
                }
            }

            return extra;
        }

        //==================== LIFECYCLE =====================
        private void Reset()
        {
            area = GetComponent<BoxCollider>();
            area.isTrigger = true;
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        private void Awake()
        {
            area = GetComponent<BoxCollider>();
        }

        private void OnEnable()
        {
            zones.Add(this);
        }

        private void OnDisable()
        {
            zones.Remove(this);
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmos()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null)
            {
                return;
            }

            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
