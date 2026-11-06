using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Builds a curved wall (a quarter pipe) so wall running can be tested on curved geometry.
    ///
    /// The surface starts flat at the object's position and curves upward along the X axis. The
    /// curve is a circular arc of the given radius. At 90 degrees it ends as a vertical wall.
    /// A Straight Length above 0 continues the surface straight on from the end of the arc, in the
    /// same mesh, so a curve and the vertical wall above it form one surface with no seam or step.
    /// The wall is solid, so the player cannot fall through it, and it is a WallRunSurface.
    ///
    /// The shape is rebuilt whenever a value changes, so it can be tuned in the Inspector.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    [RequireComponent(typeof(WallRunSurface))]
    public class CurvedWall : MonoBehaviour
    {
        //==================== SHAPE =====================
        [Header("Shape")]
        [Tooltip("Radius of the curve in world units. A larger radius is a gentler curve.")]
        [SerializeField, Min(0.5f)] private float radius = 6f;

        [Tooltip("How far the curve turns upward, in degrees. 90 ends as a vertical wall.")]
        [SerializeField, Range(10f, 90f)] private float arcAngle = 90f;

        [Tooltip("Length of a straight section that continues from the end of the curve in the same direction, in world units. With an Arc Angle of 90 it is a vertical wall joined to the curve without a seam. 0 ends the wall at the end of the curve.")]
        [SerializeField, Min(0f)] private float straightLength;

        [Tooltip("Size of the wall along the Z axis, in world units.")]
        [SerializeField, Min(0.1f)] private float width = 4f;

        [Tooltip("How far the solid body extends below the surface, in world units.")]
        [SerializeField, Min(0.1f)] private float thickness = 0.6f;

        [Tooltip("Number of straight pieces used to approximate the curve. More pieces are smoother.")]
        [SerializeField, Range(4, 64)] private int segments = 24;

        [Tooltip("Off: the wall rises toward positive X. On: it rises toward negative X.")]
        [SerializeField] private bool mirror;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Draw the surface normals while selected.")]
        [SerializeField] private bool drawNormals = true;

        private Mesh mesh;

        /// <summary>Raised after the mesh is rebuilt, so anything shaped to match it can follow.</summary>
        public event System.Action Rebuilt;

        public float Radius => radius;
        public float ArcAngle => arcAngle;
        public float Width => width;
        public float Thickness => thickness;
        public int Segments => segments;
        public bool IsMirrored => mirror;

        /// <summary>A point on the underside of the arc, in local space, the given angle (in radians) from its flat start.</summary>
        public Vector3 UndersidePoint(float angle)
        {
            float side = mirror ? -1f : 1f;
            return ArcPoint(angle, side) - ArcNormal(angle, side) * thickness;
        }

        //==================== LIFECYCLE =====================
        private void OnEnable()
        {
            Rebuild();
        }

        private void OnValidate()
        {
            // Rebuilding inside OnValidate is not allowed to touch some objects, so it is delayed.
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    Rebuild();
                }
            };
#endif
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                DestroyImmediate(mesh);
            }
        }

        //==================== BUILD =====================
        /// <summary>Creates the mesh again from the current settings.</summary>
        public void Rebuild()
        {
            if (mesh == null)
            {
                mesh = new Mesh { name = "Curved Wall", hideFlags = HideFlags.DontSave };
            }

            mesh.Clear();

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            float side = mirror ? -1f : 1f;
            float halfWidth = width * 0.5f;
            float step = arcAngle * Mathf.Deg2Rad / segments;

            // The surface profile and its outward direction at each step along the arc. The straight section,
            // when there is one, is one more step along the direction the arc ends in.
            bool hasStraight = straightLength > 0f;
            int pieces = hasStraight ? segments + 1 : segments;
            Vector3[] surface = new Vector3[pieces + 1];
            Vector3[] normal = new Vector3[pieces + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * step;
                surface[i] = ArcPoint(angle, side);
                normal[i] = ArcNormal(angle, side);
            }

            if (hasStraight)
            {
                surface[pieces] = surface[segments] + ArcTangent(arcAngle * Mathf.Deg2Rad, side) * straightLength;
                normal[pieces] = normal[segments];
            }

            Vector3 front = new Vector3(0f, 0f, -halfWidth);
            Vector3 back = new Vector3(0f, 0f, halfWidth);

            for (int i = 0; i < pieces; i++)
            {
                Vector3 topA = surface[i];
                Vector3 topB = surface[i + 1];
                Vector3 underA = surface[i] - normal[i] * thickness;
                Vector3 underB = surface[i + 1] - normal[i + 1] * thickness;
                Vector3 outward = (normal[i] + normal[i + 1]).normalized;

                AddQuad(vertices, triangles, topA + front, topB + front, topB + back, topA + back, outward);
                AddQuad(vertices, triangles, underA + front, underB + front, underB + back, underA + back, -outward);
                AddQuad(vertices, triangles, topA + back, topB + back, underB + back, underA + back, Vector3.forward);
                AddQuad(vertices, triangles, topA + front, topB + front, underB + front, underA + front, Vector3.back);
            }

            // The two ends of the wall.
            int last = pieces;
            Vector3 startDirection = new Vector3(-side, 0f, 0f);
            // The far end faces on along the surface, so a body standing on the top of a vertical wall is supported.
            Vector3 endDirection = ArcTangent(arcAngle * Mathf.Deg2Rad, side);
            AddQuad(vertices, triangles, surface[0] + front, surface[0] + back, surface[0] - normal[0] * thickness + back, surface[0] - normal[0] * thickness + front, startDirection);
            AddQuad(vertices, triangles, surface[last] + front, surface[last] + back, surface[last] - normal[last] * thickness + back, surface[last] - normal[last] * thickness + front, endDirection);

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = mesh;

            MeshCollider meshCollider = GetComponent<MeshCollider>();
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;

            Rebuilt?.Invoke();
        }

        // A point on the arc, the given angle (in radians) from its flat start.
        private Vector3 ArcPoint(float angle, float side)
        {
            return new Vector3(side * radius * Mathf.Sin(angle), radius * (1f - Mathf.Cos(angle)), 0f);
        }

        // The outward direction of the surface at that point.
        private static Vector3 ArcNormal(float angle, float side)
        {
            return new Vector3(-side * Mathf.Sin(angle), Mathf.Cos(angle), 0f);
        }

        // The direction the surface runs in at that point, away from the flat start.
        private static Vector3 ArcTangent(float angle, float side)
        {
            return new Vector3(side * Mathf.Cos(angle), Mathf.Sin(angle), 0f);
        }

        // Adds a four cornered face, turned so it faces the wanted direction.
        private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 wantedFacing)
        {
            Vector3 facing = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(facing, wantedFacing) < 0f)
            {
                (b, d) = (d, b);
            }

            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!GeneratedRoom.ShowsGizmos(this))
            {
                return;
            }

            if (!drawNormals)
            {
                return;
            }

            float side = mirror ? -1f : 1f;
            float step = arcAngle * Mathf.Deg2Rad / segments;
            Gizmos.color = Color.cyan;

            for (int i = 0; i <= segments; i += 2)
            {
                float angle = i * step;
                Vector3 point = transform.TransformPoint(ArcPoint(angle, side));
                Vector3 direction = transform.TransformDirection(ArcNormal(angle, side));
                Gizmos.DrawLine(point, point + direction);
            }

            // The straight section: its normal halfway up and its top end.
            if (straightLength > 0f)
            {
                float endAngle = arcAngle * Mathf.Deg2Rad;
                Vector3 start = ArcPoint(endAngle, side);
                Vector3 tangent = ArcTangent(endAngle, side);
                Vector3 middle = transform.TransformPoint(start + tangent * (straightLength * 0.5f));
                Gizmos.DrawLine(middle, middle + transform.TransformDirection(ArcNormal(endAngle, side)));
                Gizmos.DrawLine(transform.TransformPoint(start), transform.TransformPoint(start + tangent * straightLength));
            }
        }
    }
}
