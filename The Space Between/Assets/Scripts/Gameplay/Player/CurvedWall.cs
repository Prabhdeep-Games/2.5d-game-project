using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Builds a curved wall (a quarter pipe) so wall running can be tested on curved geometry.
    ///
    /// The surface starts flat at the object's position and curves upward along the X axis. The
    /// curve is a circular arc of the given radius. At 90 degrees it ends as a vertical wall.
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

            // The surface profile and its outward direction at each step along the arc.
            Vector3[] surface = new Vector3[segments + 1];
            Vector3[] normal = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * step;
                surface[i] = new Vector3(side * radius * Mathf.Sin(angle), radius * (1f - Mathf.Cos(angle)), 0f);
                normal[i] = new Vector3(-side * Mathf.Sin(angle), Mathf.Cos(angle), 0f);
            }

            Vector3 front = new Vector3(0f, 0f, -halfWidth);
            Vector3 back = new Vector3(0f, 0f, halfWidth);

            for (int i = 0; i < segments; i++)
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

            // The two ends of the curve.
            int last = segments;
            Vector3 startDirection = new Vector3(-side, 0f, 0f);
            Vector3 endDirection = Vector3.Cross(Vector3.forward, normal[last]).normalized * side;
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
                Vector3 point = transform.TransformPoint(new Vector3(side * radius * Mathf.Sin(angle), radius * (1f - Mathf.Cos(angle)), 0f));
                Vector3 direction = transform.TransformDirection(new Vector3(-side * Mathf.Sin(angle), Mathf.Cos(angle), 0f));
                Gizmos.DrawLine(point, point + direction);
            }
        }
    }
}
