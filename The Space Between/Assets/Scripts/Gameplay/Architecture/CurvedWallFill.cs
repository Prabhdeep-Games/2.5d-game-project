using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Fills the hollow under a <see cref="CurvedWall"/> down to its base, so the curve reads as solid from the side.
    /// The fill follows the underside of the curve exactly, so the two meet with no gap and no overlapping surfaces.
    /// It has no collider: the curve itself is what the player touches.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CurvedWallFill : MonoBehaviour
    {
        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The curve to fill under. Keep this object at the curve's position and rotation, for example as its child.")]
        [SerializeField] private CurvedWall wall;

        [Tooltip("How many world units one repeat of the texture covers. Match the blocks around it so the seams line up.")]
        [SerializeField, Min(0.1f)] private float textureWorldSize = 4f;

        private Mesh mesh;
        private CurvedWall subscribedWall;

        public CurvedWall Wall => wall;

        //==================== LIFECYCLE =====================
        private void OnEnable()
        {
            Subscribe();
            Rebuild();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled)
                {
                    Subscribe();
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

        /// <summary>Sets the curve to fill under and rebuilds.</summary>
        public void SetWall(CurvedWall newWall)
        {
            wall = newWall;
            Subscribe();
            Rebuild();
        }

        private void Subscribe()
        {
            if (subscribedWall == wall)
            {
                return;
            }

            Unsubscribe();
            subscribedWall = wall;
            if (subscribedWall != null)
            {
                subscribedWall.Rebuilt += Rebuild;
            }
        }

        private void Unsubscribe()
        {
            if (subscribedWall != null)
            {
                subscribedWall.Rebuilt -= Rebuild;
                subscribedWall = null;
            }
        }

        //==================== BUILD =====================
        /// <summary>Creates the fill again from the curve's current shape.</summary>
        public void Rebuild()
        {
            if (wall == null)
            {
                return;
            }

            if (mesh == null)
            {
                mesh = new Mesh { name = "Curved Wall Fill", hideFlags = HideFlags.DontSave };
            }

            List<Vector2> profile = BuildProfile();
            float halfWidth = wall.Width * 0.5f;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // The two side faces are fans from the bottom corner under the end of the arc. The region below a rising,
            // curving-up arc can be seen whole from that corner, so the fan never folds over itself.
            AddCap(profile, -halfWidth, Vector3.back, vertices, uvs, triangles);
            AddCap(profile, halfWidth, Vector3.forward, vertices, uvs, triangles);

            // Around the outline: underside of the arc, the end face, then the base.
            float winding = Mathf.Sign(SignedArea(profile));
            for (int i = 0; i < profile.Count; i++)
            {
                Vector2 a = profile[i];
                Vector2 b = profile[(i + 1) % profile.Count];
                AddStrip(a, b, halfWidth, winding, vertices, uvs, triangles);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        // The outline of the hollow, in the curve's local XY plane: along the underside of the arc, then down to the base
        // and back to the start. The base is level with the start of the underside.
        private List<Vector2> BuildProfile()
        {
            var profile = new List<Vector2>();
            float step = wall.ArcAngle * Mathf.Deg2Rad / wall.Segments;
            for (int i = 0; i <= wall.Segments; i++)
            {
                Vector3 point = wall.UndersidePoint(i * step);
                profile.Add(new Vector2(point.x, point.y));
            }

            float baseY = profile[0].y;
            profile.Add(new Vector2(profile[profile.Count - 1].x, baseY));
            return profile;
        }

        private void AddCap(List<Vector2> profile, float z, Vector3 facing, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            int corner = profile.Count - 1;
            for (int i = 0; i < corner - 1; i++)
            {
                AddTriangle(ToLocal(profile[corner], z), ToLocal(profile[i], z), ToLocal(profile[i + 1], z), facing, vertices, uvs, triangles);
            }
        }

        private void AddStrip(Vector2 a, Vector2 b, float halfWidth, float winding, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            if ((b - a).sqrMagnitude < 1e-8f)
            {
                return;
            }

            // The inside of a counter-clockwise outline is on its left, so outward is to the right, and the other way round.
            Vector2 along = (b - a).normalized;
            Vector2 side = new Vector2(along.y, -along.x) * winding;
            Vector3 facing = new Vector3(side.x, side.y, 0f);
            Vector3 a0 = ToLocal(a, -halfWidth);
            Vector3 b0 = ToLocal(b, -halfWidth);
            Vector3 a1 = ToLocal(a, halfWidth);
            Vector3 b1 = ToLocal(b, halfWidth);
            AddTriangle(a0, b0, b1, facing, vertices, uvs, triangles);
            AddTriangle(a0, b1, a1, facing, vertices, uvs, triangles);
        }

        // Positive when the outline runs counter-clockwise in the XY plane.
        private static float SignedArea(List<Vector2> outline)
        {
            float area = 0f;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i];
                Vector2 b = outline[(i + 1) % outline.Count];
                area += a.x * b.y - a.y * b.x;
            }

            return area * 0.5f;
        }

        private static Vector3 ToLocal(Vector2 point, float z)
        {
            return new Vector3(point.x, point.y, z);
        }

        private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f)
            {
                (b, c) = (c, b);
            }

            int start = vertices.Count;
            foreach (Vector3 vertex in new[] { a, b, c })
            {
                vertices.Add(vertex);
                uvs.Add(WorldUv(transform.TransformPoint(vertex), facing));
            }

            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        // Texture coordinates measured in world units on the two axes the face lies along, as ArchitecturalBlock does.
        private Vector2 WorldUv(Vector3 world, Vector3 facing)
        {
            Vector3 n = new Vector3(Mathf.Abs(facing.x), Mathf.Abs(facing.y), Mathf.Abs(facing.z));
            Vector2 uv = n.z >= n.x && n.z >= n.y ? new Vector2(world.x, world.y)
                : n.x >= n.y ? new Vector2(world.z, world.y)
                : new Vector2(world.x, world.z);
            return uv / textureWorldSize;
        }
    }
}
