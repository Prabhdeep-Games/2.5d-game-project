using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// Finds surfaces of different meshes that lie in the same plane, face the same way and overlap. The depth buffer cannot
    /// decide which one is in front, so they flicker in the Scene and Game views (z-fighting).
    /// </summary>
    public static class RoomGeometryCheck
    {
        // Two faces closer than this are treated as one plane. Depth precision is far coarser than this at room distances.
        private const float PlaneTolerance = 0.002f;
        private const float NormalTolerance = 0.001f;
        private const float MinimumOverlapArea = 0.0001f;

        /// <summary>One pair of flickering surfaces.</summary>
        public readonly struct Overlap
        {
            public readonly Renderer First;
            public readonly Renderer Second;
            public readonly float Area;

            public Overlap(Renderer first, Renderer second, float area)
            {
                First = first;
                Second = second;
                Area = area;
            }

            public override string ToString()
            {
                return $"'{First.name}' and '{Second.name}' share {Area:0.###} square units of surface";
            }
        }

        private struct Triangle
        {
            public int Owner;
            public Vector3 Normal;
            public float Distance;
            public Vector2 A;
            public Vector2 B;
            public Vector2 C;
            public Rect Bounds;
        }

        /// <summary>Every pair of renderers with overlapping coplanar faces. One entry per pair, with the total shared area.</summary>
        public static List<Overlap> FindOverlaps(IList<Renderer> renderers)
        {
            var triangles = new List<Triangle>();
            for (int i = 0; i < renderers.Count; i++)
            {
                CollectTriangles(renderers[i], i, triangles);
            }

            // Sorting by plane puts every candidate pair next to each other.
            triangles.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            var shared = new Dictionary<(int, int), float>();
            for (int i = 0; i < triangles.Count; i++)
            {
                Triangle first = triangles[i];
                for (int j = i + 1; j < triangles.Count && triangles[j].Distance - first.Distance <= PlaneTolerance; j++)
                {
                    Triangle second = triangles[j];
                    if (first.Owner == second.Owner || (first.Normal - second.Normal).sqrMagnitude > NormalTolerance || !first.Bounds.Overlaps(second.Bounds))
                    {
                        continue;
                    }

                    float area = SharedArea(first, second);
                    if (area > MinimumOverlapArea)
                    {
                        var key = first.Owner < second.Owner ? (first.Owner, second.Owner) : (second.Owner, first.Owner);
                        shared.TryGetValue(key, out float total);
                        shared[key] = total + area;
                    }
                }
            }

            var result = new List<Overlap>();
            foreach (KeyValuePair<(int, int), float> pair in shared)
            {
                result.Add(new Overlap(renderers[pair.Key.Item1], renderers[pair.Key.Item2], pair.Value));
            }

            return result;
        }

        /// <summary>The overlaps among every renderer under a root object, such as a generated room.</summary>
        public static List<Overlap> FindOverlaps(GameObject root)
        {
            var renderers = new List<Renderer>();
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.enabled)
                {
                    renderers.Add(renderer);
                }
            }

            return FindOverlaps(renderers);
        }

        private static void CollectTriangles(Renderer renderer, int owner, List<Triangle> triangles)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable)
            {
                return;
            }

            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = toWorld.MultiplyPoint3x4(vertices[indices[i]]);
                Vector3 b = toWorld.MultiplyPoint3x4(vertices[indices[i + 1]]);
                Vector3 c = toWorld.MultiplyPoint3x4(vertices[indices[i + 2]]);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-12f)
                {
                    continue;
                }

                Vector3 normal = cross.normalized;
                Vector2 pa = Flatten(a, normal);
                Vector2 pb = Flatten(b, normal);
                Vector2 pc = Flatten(c, normal);
                Rect bounds = Rect.MinMaxRect(
                    Mathf.Min(pa.x, pb.x, pc.x), Mathf.Min(pa.y, pb.y, pc.y),
                    Mathf.Max(pa.x, pb.x, pc.x), Mathf.Max(pa.y, pb.y, pc.y));
                triangles.Add(new Triangle { Owner = owner, Normal = normal, Distance = Vector3.Dot(normal, a), A = pa, B = pb, C = pc, Bounds = bounds });
            }
        }

        // Drops the axis the face points along most, so faces in one plane can be compared in 2D.
        private static Vector2 Flatten(Vector3 point, Vector3 normal)
        {
            Vector3 n = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            if (n.x >= n.y && n.x >= n.z)
            {
                return new Vector2(point.y, point.z);
            }

            return n.y >= n.z ? new Vector2(point.x, point.z) : new Vector2(point.x, point.y);
        }

        // Clips one triangle by the other (both are convex) and measures what is left.
        private static float SharedArea(Triangle first, Triangle second)
        {
            var polygon = new List<Vector2> { first.A, first.B, first.C };
            Vector2[] clip = CounterClockwise(second.A, second.B, second.C);
            for (int edge = 0; edge < 3 && polygon.Count > 0; edge++)
            {
                polygon = ClipByEdge(polygon, clip[edge], clip[(edge + 1) % 3]);
            }

            return Mathf.Abs(SignedArea(polygon));
        }

        private static Vector2[] CounterClockwise(Vector2 a, Vector2 b, Vector2 c)
        {
            return Cross(b - a, c - a) >= 0f ? new[] { a, b, c } : new[] { a, c, b };
        }

        private static List<Vector2> ClipByEdge(List<Vector2> polygon, Vector2 from, Vector2 to)
        {
            var result = new List<Vector2>();
            Vector2 direction = to - from;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Count];
                float currentSide = Cross(direction, current - from);
                float nextSide = Cross(direction, next - from);
                if (currentSide >= 0f)
                {
                    result.Add(current);
                }

                if ((currentSide >= 0f) != (nextSide >= 0f))
                {
                    float t = currentSide / (currentSide - nextSide);
                    result.Add(Vector2.Lerp(current, next, t));
                }
            }

            return result;
        }

        private static float SignedArea(List<Vector2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                area += Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
            }

            return area * 0.5f;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }
    }
}
