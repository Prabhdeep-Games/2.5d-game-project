using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// A piece of floor that gives way under the player. Once the player stands on it, it shakes and warms to amber for
    /// Crumble Delay, then stops holding weight and falls away. Crossing it quickly is safe; stopping on it is not.
    ///
    /// The slab comes back when the room it is in is reset (see RoomReset), so a retry always finds the same floor.
    /// Put this on an ArchitecturalBlock that spans a gap, with a TraversalHazard in the pit below.
    /// </summary>
    [RequireComponent(typeof(BoxCollider), typeof(MeshRenderer))]
    public class CrumblingSlab : MonoBehaviour
    {
        private const float StandingTolerance = 0.25f;
        private const float ShakeFrequency = 40f;
        private const float FallGravity = 25f;
        private const float HideBelowDistance = 10f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        //==================== CRUMBLE =====================
        [Header("Crumble")]
        [Tooltip("Seconds between the player first standing on the slab and the slab giving way.")]
        [SerializeField, Min(0.05f)] private float crumbleDelay = 0.6f;

        [Tooltip("How far the slab shakes while it is about to give way, in world units.")]
        [SerializeField, Min(0f)] private float shakeAmount = 0.04f;

        [Tooltip("Colour the slab warms to just before it falls. The Art Direction Bible's security amber.")]
        [SerializeField] private Color warningColor = new Color(0.827f, 0.631f, 0.302f);

        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private PlaceholderPlayerController player;

        [Tooltip("The room whose reset brings the slab back. Empty uses the Room Reset whose area contains the slab.")]
        [SerializeField] private RoomReset room;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("What the slab is doing right now.")]
        [ReadOnly, SerializeField] private State state = State.Holding;

        private enum State
        {
            Holding,
            Crumbling,
            Fallen
        }

        private BoxCollider box;
        private MeshRenderer meshRenderer;
        private CharacterController playerCapsule;
        private MaterialPropertyBlock block;
        private Color baseColor = Color.white;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private Bounds restBounds;
        private float crumbleStart;
        private float fallSpeed;

        /// <summary>True from the moment the slab stops holding weight until its room is reset.</summary>
        public bool HasFallen => state == State.Fallen;

        /// <summary>True while the slab is shaking, about to give way.</summary>
        public bool IsCrumbling => state == State.Crumbling;

        public float CrumbleDelay => crumbleDelay;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            box = GetComponent<BoxCollider>();
            meshRenderer = GetComponent<MeshRenderer>();
            block = new MaterialPropertyBlock();
            if (meshRenderer.sharedMaterial != null && meshRenderer.sharedMaterial.HasProperty(BaseColorId))
            {
                baseColor = meshRenderer.sharedMaterial.GetColor(BaseColorId);
            }

            if (player == null)
            {
                player = FindAnyObjectByType<PlaceholderPlayerController>();
            }

            playerCapsule = player != null ? player.GetComponent<CharacterController>() : null;
        }

        private void Start()
        {
            restPosition = transform.position;
            restRotation = transform.rotation;
            restBounds = box.bounds;

            if (room == null)
            {
                room = RoomReset.FindContaining(restBounds.center);
            }

            if (room != null)
            {
                room.WasReset += Restore;
            }
        }

        private void OnDestroy()
        {
            if (room != null)
            {
                room.WasReset -= Restore;
            }
        }

        private void Update()
        {
            switch (state)
            {
                case State.Holding:
                    if (PlayerStandingOn())
                    {
                        state = State.Crumbling;
                        crumbleStart = Time.time;
                    }

                    break;

                case State.Crumbling:
                    Shake();
                    if (Time.time - crumbleStart >= crumbleDelay)
                    {
                        GiveWay();
                    }

                    break;

                case State.Fallen:
                    Fall();
                    break;
            }
        }

        //==================== CRUMBLING =====================
        // Standing means grounded with the feet on the slab's top, anywhere along it.
        private bool PlayerStandingOn()
        {
            if (player == null || playerCapsule == null || !playerCapsule.enabled || !player.IsGrounded)
            {
                return false;
            }

            Bounds feet = playerCapsule.bounds;
            bool above = feet.max.x > restBounds.min.x && feet.min.x < restBounds.max.x;
            return above && Mathf.Abs(feet.min.y - restBounds.max.y) <= StandingTolerance;
        }

        private void Shake()
        {
            float progress = Mathf.Clamp01((Time.time - crumbleStart) / crumbleDelay);
            float offset = Mathf.Sin(Time.time * ShakeFrequency) * shakeAmount * progress;
            transform.position = restPosition + new Vector3(offset, -offset * 0.5f, 0f);
            SetColor(Color.Lerp(baseColor, warningColor, progress));
        }

        // The collider goes first, so the player drops at once; the slab itself follows as debris.
        private void GiveWay()
        {
            state = State.Fallen;
            box.enabled = false;
            fallSpeed = 0f;
            transform.position = restPosition;
        }

        private void Fall()
        {
            if (!meshRenderer.enabled)
            {
                return;
            }

            fallSpeed += FallGravity * Time.deltaTime;
            transform.position += Vector3.down * (fallSpeed * Time.deltaTime);
            transform.Rotate(Vector3.forward, 20f * Time.deltaTime);
            if (restPosition.y - transform.position.y > HideBelowDistance)
            {
                meshRenderer.enabled = false;
            }
        }

        /// <summary>Puts the slab back as it started. Called when its room is reset.</summary>
        public void Restore()
        {
            state = State.Holding;
            transform.SetPositionAndRotation(restPosition, restRotation);
            box.enabled = true;
            meshRenderer.enabled = true;
            SetColor(baseColor);
        }

        private void SetColor(Color color)
        {
            meshRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            meshRenderer.SetPropertyBlock(block);
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            if (!GeneratedRoom.ShowsGizmos(this))
            {
                return;
            }

            BoxCollider collider = GetComponent<BoxCollider>();
            if (collider == null)
            {
                return;
            }

            Gizmos.color = warningColor;
            Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
        }
    }
}
