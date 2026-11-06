using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// One reusable piece of the building: a wall, slab, column or beam, set by its size instead of its scale.
    ///
    /// A scaled cube stretches its texture, so concrete seams would change size from wall to wall. This builds its own
    /// box with texture coordinates measured in world units, so one material keeps the same seam spacing on every
    /// block and the seams of neighbouring blocks line up. That lets a compact set of materials cover a whole level.
    ///
    /// The shape is rebuilt whenever a value changes, so it can be tuned in the Inspector. Keep the object's scale at
    /// 1 and its rotation at zero; use Size instead.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider))]
    public class ArchitecturalBlock : MonoBehaviour
    {
        private const int VerticesPerFace = 4;
        private const int FaceCount = 6;

        //==================== SHAPE =====================
        [Header("Shape")]
        [Tooltip("Size of the block in world units. The object's position is its centre.")]
        [SerializeField] private Vector3 size = new Vector3(4f, 4f, 4f);

        [Tooltip("How many world units one repeat of the texture covers. Keep it the same on all blocks that share a material, so their seams line up.")]
        [SerializeField, Min(0.1f)] private float textureWorldSize = 4f;

        //==================== COLLISION =====================
        [Header("Collision")]
        [Tooltip("Whether the block stops the player and blocks sight. Turn it off for pieces that are only seen, such as background detail.")]
        [SerializeField] private bool solid = true;

        private Mesh mesh;
        private Vector3 builtAt;

        public Vector3 Size => size;

        public bool Solid => solid;

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

#if UNITY_EDITOR
        // Moving a block in the Scene view changes which part of the texture it shows, so its seams keep lining up.
        private void Update()
        {
            if (!Application.isPlaying && transform.position != builtAt)
            {
                Rebuild();
            }
        }
#endif

        private void OnDestroy()
        {
            if (mesh != null)
            {
                DestroyImmediate(mesh);
            }
        }

        //==================== BUILD =====================
        /// <summary>Creates the box again from the current settings.</summary>
        public void Rebuild()
        {
            if (mesh == null)
            {
                mesh = new Mesh { name = "Architectural Block", hideFlags = HideFlags.DontSave };
            }

            builtAt = transform.position;
            Vector3 half = size * 0.5f;
            Vector3[] vertices = new Vector3[VerticesPerFace * FaceCount];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[FaceCount * 6];

            AddFace(0, Vector3.back, Vector3.right, Vector3.up, half, vertices, normals, uvs, triangles);
            AddFace(1, Vector3.forward, Vector3.left, Vector3.up, half, vertices, normals, uvs, triangles);
            AddFace(2, Vector3.left, Vector3.back, Vector3.up, half, vertices, normals, uvs, triangles);
            AddFace(3, Vector3.right, Vector3.forward, Vector3.up, half, vertices, normals, uvs, triangles);
            AddFace(4, Vector3.up, Vector3.right, Vector3.forward, half, vertices, normals, uvs, triangles);
            AddFace(5, Vector3.down, Vector3.right, Vector3.back, half, vertices, normals, uvs, triangles);

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = mesh;

            BoxCollider box = GetComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = size;
            box.enabled = solid;
        }

        // One side of the box. Its texture position comes from where it is in the world, which is what keeps seams aligned.
        private void AddFace(int face, Vector3 normal, Vector3 right, Vector3 up, Vector3 half, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles)
        {
            int start = face * VerticesPerFace;
            Vector3 center = Vector3.Scale(normal, half);
            Vector3 toRight = Vector3.Scale(right, half);
            Vector3 toUp = Vector3.Scale(up, half);

            vertices[start] = center - toRight - toUp;
            vertices[start + 1] = center + toRight - toUp;
            vertices[start + 2] = center + toRight + toUp;
            vertices[start + 3] = center - toRight + toUp;

            for (int i = 0; i < VerticesPerFace; i++)
            {
                Vector3 world = transform.position + vertices[start + i];
                normals[start + i] = normal;
                uvs[start + i] = new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, up)) / textureWorldSize;
            }

            int index = face * 6;
            triangles[index] = start;
            triangles[index + 1] = start + 2;
            triangles[index + 2] = start + 1;
            triangles[index + 3] = start;
            triangles[index + 4] = start + 3;
            triangles[index + 5] = start + 2;
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!GeneratedRoom.ShowsGizmos(this))
            {
                return;
            }

            Gizmos.color = solid ? Color.white : Color.gray;
            Gizmos.DrawWireCube(transform.position, size);
        }
    }
}
