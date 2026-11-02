using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace TheSpaceBetween
{
    /// <summary>
    /// Surveillance: watches a cone of the room and puts linked mechanisms into lockdown while it sees the player.
    /// A sealed door it watches closes and stays shut until the camera has not seen the player for Release Delay
    /// seconds.
    ///
    /// The camera follows the same sight rule as swapping: glass does not hide the player, solid objects do.
    /// A crate between the camera and the player therefore hides them, and a swap leaves a crate exactly where
    /// the player stood.
    ///
    /// The watched area is drawn as a see-through cone that is cut off by whatever blocks the view, so the player
    /// can read the blind spots. It is yellow while watching and red while the alarm holds the lockdown.
    /// The camera is itself a mechanism: a control panel can switch it off, which ends any lockdown at once.
    ///
    /// With Hold Until Cleared on, being seen once is enough: the lockdown stays until ClearAlarm is called (wire
    /// the room reset to it) or the camera is switched off. Without it, waiting out of view would always open the
    /// door again.
    /// </summary>
    public class SecurityCamera : Mechanism
    {
        private const float SampleSpread = 0.8f;   // Head and feet samples, as a share of the half height.
        private const int MinConeRays = 4;
        private const int AlarmFontSize = 64;
        private const string BuiltinFontName = "LegacyRuntime.ttf";

        //==================== POWER =====================
        [Header("Power")]
        [Tooltip("Whether the camera starts switched on. A control panel can switch it.")]
        [SerializeField] private bool startsOn = true;

        //==================== VIEW =====================
        [Header("View")]
        [Tooltip("Direction the camera looks, in the local space of this object. Kept in the XY game plane.")]
        [SerializeField] private Vector3 localViewDirection = Vector3.down;

        [Tooltip("Full width of the watched cone, in degrees.")]
        [SerializeField, Range(5f, 180f)] private float viewAngle = 60f;

        [Tooltip("How far the camera sees, in world units.")]
        [SerializeField, Min(0.5f)] private float range = 10f;

        [Tooltip("Layers that hide what is behind them. Leave Glass out, so the camera sees through glass like the swap does.")]
        [SerializeField] private LayerMask sightBlockers = ~0;

        //==================== WATCHED =====================
        [Header("Watched")]
        [Tooltip("What the camera looks for. Empty means the player (the object with a PlaceholderPlayerController).")]
        [SerializeField] private List<Transform> watched = new List<Transform>();

        //==================== RESPONSE =====================
        [Header("Response")]
        [Tooltip("Mechanisms held in lockdown while the camera sees a watched object: doors close, platforms stop.")]
        [SerializeField] private List<Mechanism> locksDown = new List<Mechanism>();

        [Tooltip("Seconds the lockdown lasts after the camera last saw a watched object. Ignored when Hold Until Cleared is on.")]
        [SerializeField, Min(0f)] private float releaseDelay = 1.5f;

        [Tooltip("Keep the lockdown after the watched object leaves the view, until ClearAlarm is called (for example by the room reset) or the camera is switched off.")]
        [SerializeField] private bool holdUntilCleared;

        //==================== FEEDBACK =====================
        [Header("Feedback")]
        [Tooltip("Draw the watched area as a cone.")]
        [SerializeField] private bool showViewCone = true;

        [Tooltip("Material for the cone. Use an unlit, transparent material; its colour is set while playing.")]
        [SerializeField] private Material viewMaterial;

        [Tooltip("Cone colour while watching.")]
        [SerializeField] private Color watchingColor = new Color(1f, 0.85f, 0.2f, 0.18f);

        [Tooltip("Cone colour while the alarm holds the lockdown.")]
        [SerializeField] private Color alarmColor = new Color(1f, 0.2f, 0.15f, 0.32f);

        [Tooltip("Number of rays that shape the cone. More follows edges more closely.")]
        [SerializeField, Min(MinConeRays)] private int coneRays = 48;

        [Tooltip("World Z of the cone. Slightly behind the player (z 0), so the player is drawn in front of it.")]
        [SerializeField] private float coneDepth = 0.6f;

        [Tooltip("Text shown below the camera while the alarm holds the lockdown. Empty shows nothing.")]
        [SerializeField] private string alarmMessage = "Spotted";

        [Tooltip("Where the alarm text appears, relative to the camera.")]
        [SerializeField] private Vector3 alarmTextOffset = new Vector3(0f, -1f, 0f);

        [Tooltip("Size of the alarm text in world units per font pixel.")]
        [SerializeField, Min(0.01f)] private float alarmTextSize = 0.08f;

        [Tooltip("Draw a cable from the camera to each mechanism it locks down.")]
        [SerializeField] private bool showWires = true;

        [Tooltip("Material for the cables. Use an unlit material; its colour is set while playing.")]
        [SerializeField] private Material wireMaterial;

        [Tooltip("Cable thickness in world units.")]
        [SerializeField, Min(0.01f)] private float wireWidth = 0.06f;

        [Tooltip("World Z of the cables.")]
        [SerializeField] private float wireDepth = 1.2f;

        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked when the camera spots a watched object and the lockdown starts.")]
        [SerializeField] private UnityEvent spottedEvent;

        [Tooltip("Invoked when the lockdown ends because nothing has been seen for Release Delay seconds.")]
        [SerializeField] private UnityEvent releasedEvent;

        //==================== DEBUG =====================
        [Header("Camera State")]
        [Tooltip("Whether a watched object is in view this frame.")]
        [ReadOnly, SerializeField] private bool isSeeing;

        [Tooltip("Whether the alarm is holding the lockdown.")]
        [ReadOnly, SerializeField] private bool isAlarmed;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly PowerWires wires = new PowerWires();
        private readonly List<Collider[]> watchedColliders = new List<Collider[]>();
        private MaterialPropertyBlock coneBlock;
        private MeshRenderer coneRenderer;
        private Mesh coneMesh;
        private Vector3[] coneVertices;
        private TextMesh alarmText;
        private float lastSeenTime = float.NegativeInfinity;

        //==================== OUTPUTS =====================
        public bool IsSeeing => isSeeing;

        public bool IsAlarmed => isAlarmed;

        /// <summary>The alarm text shown right now, or empty when it is hidden.</summary>
        public string AlarmText => alarmText != null && alarmText.gameObject.activeSelf ? alarmText.text : string.Empty;

        /// <summary>
        /// Ends a held alarm and releases the lockdown, unless a watched object is in view right now.
        /// Wire a room reset to it, so the room is fair again after the player is put back.
        /// </summary>
        public void ClearAlarm()
        {
            if (IsActive && SeesAnyWatched())
            {
                return;
            }

            isSeeing = false;
            EndAlarm();
        }

        public Vector3 ViewDirection
        {
            get
            {
                Vector3 direction = transform.TransformDirection(localViewDirection);
                direction.z = 0f;
                return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.down;
            }
        }

        /// <summary>True when the point is inside the cone and nothing on the sight blocker layers hides it.</summary>
        public bool CanSee(Vector3 point, Transform owner = null)
        {
            Vector3 origin = transform.position;
            Vector3 toPoint = point - origin;
            float distance = toPoint.magnitude;
            if (distance > range || Vector3.Angle(ViewDirection, toPoint) > viewAngle * 0.5f)
            {
                return false;
            }

            if (distance < Mathf.Epsilon)
            {
                return true;
            }

            if (!Physics.Raycast(origin, toPoint / distance, out RaycastHit hit, distance, sightBlockers, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return owner != null && hit.transform.IsChildOf(owner);
        }

        //==================== MECHANISM =====================
        protected override bool StartsPowered => startsOn;

        protected override void OnActiveChanged(bool active)
        {
            if (!active)
            {
                isSeeing = false;
                EndAlarm();
            }

            Log(active ? "watching" : "switched off");
        }

        //==================== LIFECYCLE =====================
        private void Reset()
        {
            sightBlockers = ~LayerMask.GetMask("Glass", "Ignore Raycast", "UI");
        }

        protected override void Awake()
        {
            base.Awake();
            coneBlock = new MaterialPropertyBlock();
            if (showViewCone && viewMaterial != null)
            {
                CreateCone();
            }

            if (!string.IsNullOrEmpty(alarmMessage))
            {
                alarmText = CreateAlarmText();
            }
        }

        private void Start()
        {
            if (watched.Count == 0)
            {
                PlaceholderPlayerController player = FindAnyObjectByType<PlaceholderPlayerController>();
                if (player != null)
                {
                    watched.Add(player.transform);
                }
            }

            watchedColliders.Clear();
            for (int i = 0; i < watched.Count; i++)
            {
                watchedColliders.Add(watched[i] != null ? watched[i].GetComponentsInChildren<Collider>() : new Collider[0]);
            }

            if (showWires)
            {
                wires.Build(transform, locksDown, wireMaterial, wireWidth, wireDepth);
            }

            UpdateColors();
        }

        protected override void OnDisable()
        {
            EndAlarm();
            base.OnDisable();
        }

        private void OnDestroy()
        {
            if (coneMesh != null)
            {
                Destroy(coneMesh);
            }
        }

        private void Update()
        {
            if (IsActive)
            {
                isSeeing = SeesAnyWatched();
                if (isSeeing)
                {
                    lastSeenTime = Time.time;
                    StartAlarm();
                }
                else if (isAlarmed && !holdUntilCleared && Time.time - lastSeenTime >= releaseDelay)
                {
                    EndAlarm();
                }
            }

            UpdateCone();
            UpdateColors();
        }

        //==================== DETECTION =====================
        private bool SeesAnyWatched()
        {
            for (int i = 0; i < watched.Count; i++)
            {
                Transform target = watched[i];
                if (target == null)
                {
                    continue;
                }

                // A body in the middle of a smooth swap is not anywhere yet. Game time is stopped, so the alarm holds.
                Swappable swappable = target.GetComponent<Swappable>();
                if (swappable != null && swappable.IsFrozen)
                {
                    continue;
                }

                if (!TryGetBounds(watchedColliders[i], out Bounds bounds))
                {
                    continue;
                }

                // Centre, head and feet: a target half hidden behind a crate is still seen.
                Vector3 spread = Vector3.up * bounds.extents.y * SampleSpread;
                if (CanSee(bounds.center, target) || CanSee(bounds.center + spread, target) || CanSee(bounds.center - spread, target))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetBounds(Collider[] colliders, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null || colliders[i].isTrigger || !colliders[i].enabled)
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

        private void StartAlarm()
        {
            if (isAlarmed)
            {
                return;
            }

            isAlarmed = true;
            for (int i = 0; i < locksDown.Count; i++)
            {
                if (locksDown[i] != null)
                {
                    locksDown[i].SetLockdown(this, true);
                }
            }

            Log("spotted a watched object");
            spottedEvent?.Invoke();
        }

        private void EndAlarm()
        {
            if (!isAlarmed)
            {
                return;
            }

            isAlarmed = false;
            for (int i = 0; i < locksDown.Count; i++)
            {
                if (locksDown[i] != null)
                {
                    locksDown[i].SetLockdown(this, false);
                }
            }

            Log("released the lockdown");
            releasedEvent?.Invoke();
        }

        //==================== CONE =====================
        private void CreateCone()
        {
            var holder = new GameObject("View Cone");
            holder.transform.SetParent(transform, false);
            holder.layer = LayerMask.NameToLayer("Ignore Raycast");

            coneMesh = new Mesh { name = "Security Camera View" };
            coneMesh.MarkDynamic();
            holder.AddComponent<MeshFilter>().sharedMesh = coneMesh;
            coneRenderer = holder.AddComponent<MeshRenderer>();
            coneRenderer.sharedMaterial = viewMaterial;
            coneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coneRenderer.receiveShadows = false;

            int rays = Mathf.Max(coneRays, MinConeRays);
            coneVertices = new Vector3[rays + 2];
            int[] triangles = new int[rays * 3];
            for (int i = 0; i < rays; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 2;
                triangles[i * 3 + 2] = i + 1;
            }

            coneMesh.vertices = coneVertices;
            coneMesh.triangles = triangles;
        }

        // The cone is cut off where a ray meets something on the sight blocker layers, so blind spots show.
        private void UpdateCone()
        {
            if (coneRenderer == null)
            {
                return;
            }

            coneRenderer.enabled = IsActive;
            if (!IsActive)
            {
                return;
            }

            Transform holder = coneRenderer.transform;
            Vector3 origin = transform.position;
            Vector3 direction = ViewDirection;
            int rays = coneVertices.Length - 2;

            coneVertices[0] = holder.InverseTransformPoint(new Vector3(origin.x, origin.y, coneDepth));
            for (int i = 0; i <= rays; i++)
            {
                float angle = Mathf.Lerp(-viewAngle * 0.5f, viewAngle * 0.5f, i / (float)rays);
                Vector3 ray = Quaternion.AngleAxis(angle, Vector3.forward) * direction;
                float reach = Physics.Raycast(origin, ray, out RaycastHit hit, range, sightBlockers, QueryTriggerInteraction.Ignore) ? hit.distance : range;
                Vector3 end = origin + ray * reach;
                coneVertices[i + 1] = holder.InverseTransformPoint(new Vector3(end.x, end.y, coneDepth));
            }

            coneMesh.vertices = coneVertices;
            coneMesh.RecalculateBounds();
        }

        private TextMesh CreateAlarmText()
        {
            var holder = new GameObject("Alarm Text");
            holder.transform.SetParent(transform, false);
            holder.transform.localPosition = alarmTextOffset;
            holder.layer = LayerMask.NameToLayer("Ignore Raycast");

            TextMesh text = holder.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
            text.font = font;
            holder.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.fontSize = AlarmFontSize;
            text.characterSize = alarmTextSize;
            text.anchor = TextAnchor.UpperCenter;
            text.alignment = TextAlignment.Center;
            text.text = alarmMessage;
            holder.SetActive(false);
            return text;
        }

        private void UpdateColors()
        {
            Color color = isAlarmed ? alarmColor : watchingColor;
            Color opaque = new Color(color.r, color.g, color.b, 1f);
            wires.SetColor(opaque);

            if (alarmText != null)
            {
                alarmText.gameObject.SetActive(isAlarmed);
                alarmText.color = opaque;
            }

            if (coneRenderer == null)
            {
                return;
            }

            coneRenderer.GetPropertyBlock(coneBlock);
            coneBlock.SetColor(BaseColorId, color);
            coneRenderer.SetPropertyBlock(coneBlock);
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmosSelected()
        {
            Vector3 origin = transform.position;
            Vector3 direction = ViewDirection;
            Vector3 left = Quaternion.AngleAxis(-viewAngle * 0.5f, Vector3.forward) * direction * range;
            Vector3 right = Quaternion.AngleAxis(viewAngle * 0.5f, Vector3.forward) * direction * range;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, origin + left);
            Gizmos.DrawLine(origin, origin + right);
            Gizmos.DrawLine(origin + left, origin + right);
        }
    }
}
