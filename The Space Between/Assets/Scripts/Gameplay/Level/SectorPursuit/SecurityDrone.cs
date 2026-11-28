using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// A placeholder security drone that hunts the protagonist through Sector Pursuit. It flies along a DroneRoute, so it
    /// never has to find a way around anything, and it is moved directly, with no collider and no physics.
    ///
    /// - Dormant until Activate, hidden in its hatch.
    /// - Searching: drifts along the route sweeping its light, until the player is within Detect Range with a clear line
    ///   of sight (and at least Min Search Seconds have passed).
    /// - Pursuing: follows at Pursuit Speed, a little slower than the player's run and a little faster than the walk, so
    ///   running pulls away and walking or stopping lets it close in. More than Leash Gap behind and out of the camera's
    ///   view it catches up quickly, so it is never left far behind and is never seen jumping forward.
    /// - Within Capture Gap of the player for Capture Seconds, it catches them: the player's room fails with the title
    ///   CONTAINED, and the drone goes back behind the room's start while the view is dark.
    /// - Trailing: once the player is past Capture Until (climbing the shaft), it follows up the route without catching,
    ///   kept Trail Gap behind, so it is glimpsed below.
    /// - Holding and Exiting: the endpoint stops it, then sends it along the rest of the route at Exit Speed.
    ///
    /// The Model child is only a placeholder: the final model and animation can replace its children without touching
    /// this script. The sound slot stays empty until the final sound is supplied.
    /// </summary>
    public sealed class SecurityDrone : MonoBehaviour
    {
        /// <summary>What the drone is doing.</summary>
        public enum DroneState
        {
            Dormant,
            Searching,
            Pursuing,
            Trailing,
            Holding,
            Exiting,
            Gone
        }

        private const string CaughtTitle = "CONTAINED";

        // Only one catch at a time: two drones arriving together must not fail the room twice.
        private static float catchLockedUntil = -1f;

        //==================== REFERENCES =====================
        [Header("References")]
        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private PlaceholderPlayerController player;

        [Tooltip("The route the drone flies along.")]
        [SerializeField] private DroneRoute route;

        [Tooltip("The visible drone. Only its children are the placeholder model; replace them with the final model.")]
        [SerializeField] private Transform model;

        [Tooltip("The search light. Amber, the facility's security colour.")]
        [SerializeField] private Light searchLight;

        [Tooltip("Optional: the drone's mechanical hum (slot, empty until the final sound is supplied). Plays while it flies.")]
        [SerializeField] private AudioSource humSlot;

        //==================== PLACE ON THE ROUTE =====================
        [Header("Place On The Route")]
        [Tooltip("Where the drone waits until it is activated, as a distance along the route.")]
        [SerializeField, Min(0f)] private float startDistance;

        [Tooltip("Extra distance behind the lead drone, so two drones do not fly inside each other.")]
        [SerializeField, Min(0f)] private float slotBehind;

        [Tooltip("Height added to the route, so drones fly at slightly different heights.")]
        [SerializeField] private float heightOffset;

        //==================== SEARCH =====================
        [Header("Search")]
        [SerializeField, Min(0f)] private float searchSpeed = 3f;

        [Tooltip("The player is found within this distance along the route, with a clear line of sight.")]
        [SerializeField, Min(1f)] private float detectRange = 20f;

        [Tooltip("Shortest search before the pursuit starts, in seconds, so the drone is seen looking before it hunts.")]
        [SerializeField, Min(0f)] private float minSearchSeconds = 1.5f;

        //==================== PURSUIT =====================
        [Header("Pursuit")]
        [Tooltip("Speed while pursuing, in units per second. The player walks at 6 and runs at 7.6.")]
        [SerializeField, Min(0f)] private float pursuitSpeed = 6.6f;

        [Tooltip("Speed while catching up out of view.")]
        [SerializeField, Min(0f)] private float catchUpSpeed = 11f;

        [Tooltip("Further behind than this, and out of view, the drone catches up.")]
        [SerializeField, Min(1f)] private float leashGap = 9f;

        [Tooltip("The drone catches the player when it is this close along the route...")]
        [SerializeField, Min(0.1f)] private float captureGap = 2.5f;

        [Tooltip("...for this many seconds in a row.")]
        [SerializeField, Min(0.1f)] private float captureSeconds = 1f;

        [Tooltip("Past this distance along the route the player is climbing: no more catching, only trailing.")]
        [SerializeField] private float captureUntil = float.PositiveInfinity;

        [Tooltip("After a room is restarted, the drone waits this far behind the room's start.")]
        [SerializeField, Min(0f)] private float resetGap = 12f;

        //==================== TRAIL AND EXIT =====================
        [Header("Trail And Exit")]
        [SerializeField, Min(1f)] private float trailGap = 12f;

        [SerializeField, Min(0f)] private float trailSpeed = 6.5f;

        [SerializeField, Min(0f)] private float exitSpeed = 7f;

        [Tooltip("At the endpoint, a drone further back than this (plus 8) waits this far behind instead, out of view.")]
        [SerializeField, Min(0f)] private float holdBehind = 22f;

        //==================== MOTION =====================
        [Header("Motion")]
        [Tooltip("How far the drone bobs up and down, in world units.")]
        [SerializeField, Min(0f)] private float bob = 0.12f;

        [Tooltip("How much lower it flies when right behind the player, reaching for them.")]
        [SerializeField, Min(0f)] private float reachDown = 1.2f;

        //==================== DEBUG =====================
        [Header("Debug")]
        [SerializeField] private bool logDrone;

        [ReadOnly, SerializeField] private DroneState state = DroneState.Dormant;
        [ReadOnly, SerializeField] private float routeDistance;
        [ReadOnly, SerializeField] private float playerDistance;
        [ReadOnly, SerializeField] private float gap;
        [ReadOnly, SerializeField] private int catches;

        private readonly List<RoomReset> rooms = new List<RoomReset>();
        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly Plane[] frustum = new Plane[6];
        private float stateSeconds;
        private float captureTimer;
        private float playerDistanceHint = -1f;
        private float bobPhase;
        private float reach;

        public DroneState State => state;

        /// <summary>How far along the route the drone is.</summary>
        public float RouteDistance => routeDistance;

        /// <summary>How far ahead of the drone the player is, along the route.</summary>
        public float Gap => gap;

        public float PursuitSpeed => pursuitSpeed;

        public float CaptureGap => captureGap;

        public float CaptureSeconds => captureSeconds;

        public int Catches => catches;

        public DroneRoute Route => route;

        //==================== LIFECYCLE =====================
        private void Awake()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlaceholderPlayerController>();
            }

            if (model != null)
            {
                model.GetComponentsInChildren(true, renderers);
            }

            bobPhase = Random.Range(0f, Mathf.PI * 2f);
        }

        private void Start()
        {
            foreach (RoomReset room in FindObjectsByType<RoomReset>(FindObjectsSortMode.None))
            {
                RoomReset captured = room;
                rooms.Add(room);
                room.WasReset += () => OnRoomReset(captured);
            }

            routeDistance = startDistance;
            Place(0f);
            SetLight(false);
        }

        private void Update()
        {
            if (player == null || route == null || state == DroneState.Dormant || state == DroneState.Gone)
            {
                return;
            }

            // While the view is dark for a restart or a level change, the drones wait where they are.
            FacilityUI ui = FacilityUI.Current;
            float dt = ui != null && (ui.IsReturning || ui.IsLeaving) ? 0f : Time.deltaTime;
            stateSeconds += dt;
            playerDistance = route.DistanceOf(player.transform.position + Vector3.up, playerDistanceHint, 14f);
            playerDistanceHint = playerDistance;
            gap = playerDistance - routeDistance;

            switch (state)
            {
                case DroneState.Searching:
                    Search(dt);
                    break;
                case DroneState.Pursuing:
                    Pursue(dt);
                    break;
                case DroneState.Trailing:
                    Trail(dt);
                    break;
                case DroneState.Exiting:
                    Exit(dt);
                    break;
            }

            Place(dt);
        }

        //==================== CONTROL =====================
        /// <summary>Wakes the drone: it leaves its hatch and starts searching.</summary>
        public void Activate()
        {
            if (state != DroneState.Dormant)
            {
                return;
            }

            SetState(DroneState.Searching);
            SetLight(true);
            if (humSlot != null && humSlot.clip != null)
            {
                humSlot.Play();
            }
        }

        /// <summary>
        /// Stops the drone for the endpoint. A drone left far behind (more than Hold Behind plus a margin, for example after
        /// Continue straight to the platform) first moves to Hold Behind, but only while it is out of view.
        /// </summary>
        public void Hold()
        {
            if (state == DroneState.Dormant || state == DroneState.Gone)
            {
                return;
            }

            if (route != null && !IsVisible())
            {
                float behind = route.DistanceOf(player.transform.position + Vector3.up) - routeDistance;
                if (behind > holdBehind + 8f)
                {
                    PlaceBehindPlayer(holdBehind);
                }
            }

            SetState(DroneState.Holding);
        }

        /// <summary>Sends the drone along the rest of the route, whatever the player is doing.</summary>
        public void BeginExit()
        {
            if (state == DroneState.Gone)
            {
                return;
            }

            SetLight(true);
            SetState(DroneState.Exiting);
        }

        /// <summary>
        /// Puts the drone Behind units behind the player along the route, pursuing (or, with Search Again, looking for the
        /// player first, as after a restart). Only for a moment the view is dark or the drone is out of sight.
        /// </summary>
        public void PlaceBehindPlayer(float behind)
        {
            PlaceBehindPlayer(behind, false);
        }

        /// <inheritdoc cref="PlaceBehindPlayer(float)"/>
        public void PlaceBehindPlayer(float behind, bool searchAgain)
        {
            if (state == DroneState.Dormant || state == DroneState.Gone || route == null)
            {
                return;
            }

            playerDistance = route.DistanceOf(player.transform.position + Vector3.up);
            playerDistanceHint = playerDistance;
            routeDistance = Mathf.Max(playerDistance - behind - slotBehind, 0f);
            captureTimer = 0f;
            reach = 0f;
            SetState(playerDistance > captureUntil ? DroneState.Trailing : searchAgain ? DroneState.Searching : DroneState.Pursuing);
            Place(0f);
        }

        //==================== BEHAVIOUR =====================
        private void Search(float dt)
        {
            routeDistance = Mathf.Min(routeDistance + searchSpeed * dt, Mathf.Max(playerDistance - slotBehind - captureGap, routeDistance));
            SweepLight();
            if (stateSeconds >= minSearchSeconds && gap <= detectRange && SeesPlayer())
            {
                SetState(DroneState.Pursuing);
            }
        }

        private void Pursue(float dt)
        {
            if (playerDistance > captureUntil)
            {
                SetState(DroneState.Trailing);
                return;
            }

            float wanted = playerDistance - slotBehind;
            float speed = gap - slotBehind > leashGap && !IsVisible() ? catchUpSpeed : pursuitSpeed;
            routeDistance = Mathf.Min(routeDistance + speed * dt, Mathf.Max(wanted - 0.5f, routeDistance));
            AimLight();

            // Reaching down for the player when right behind them.
            float closeness = Mathf.Clamp01(1f - (gap - slotBehind) / (captureGap * 2f));
            reach = Mathf.MoveTowards(reach, closeness * reachDown, 2f * dt);

            bool inReach = gap - slotBehind <= captureGap && gap > -1f && ControlIsWithPlayer();
            captureTimer = inReach ? captureTimer + dt : 0f;
            if (captureTimer >= captureSeconds)
            {
                Catch();
            }
        }

        private void Trail(float dt)
        {
            float wanted = playerDistance - trailGap - slotBehind;
            float speed = wanted - routeDistance > leashGap && !IsVisible() ? catchUpSpeed : trailSpeed;
            routeDistance = Mathf.Min(routeDistance + speed * dt, Mathf.Max(wanted, routeDistance));
            reach = Mathf.MoveTowards(reach, 0f, 2f * dt);
            AimLight();

            // Back in the corridors (the player went back down): the hunt is on again.
            if (playerDistance < captureUntil - 2f)
            {
                SetState(DroneState.Pursuing);
            }
        }

        private void Exit(float dt)
        {
            routeDistance += exitSpeed * dt;
            reach = Mathf.MoveTowards(reach, 0f, 2f * dt);
            SweepLight();
            if (routeDistance >= route.Length)
            {
                routeDistance = route.Length;
                SetState(DroneState.Gone);
                SetLight(false);
                if (model != null)
                {
                    model.gameObject.SetActive(false);
                }
            }
        }

        private bool ControlIsWithPlayer()
        {
            FacilityUI ui = FacilityUI.Current;
            if (ui != null && (ui.IsReturning || ui.IsLeaving || ui.IsCutscene || ui.IsPaused || ui.IsEnding))
            {
                return false;
            }

            return player.isActiveAndEnabled && !player.ControlLocked;
        }

        private void Catch()
        {
            captureTimer = 0f;
            if (Time.unscaledTime < catchLockedUntil)
            {
                return;
            }

            catchLockedUntil = Time.unscaledTime + 2f;
            catches++;
            RoomReset room = RoomReset.FindContaining(player.transform.position + Vector3.up);
            if (logDrone)
            {
                Debug.Log($"[{nameof(SecurityDrone)}] '{name}' caught the player in '{(room != null ? room.name : "no room")}'.", this);
            }

            SlowMotionMoment.CancelActive();
            if (room != null)
            {
                room.Fail(CaughtTitle);
            }
        }

        // The view is dark while a room restarts, so the drone simply waits behind the room's start again.
        private void OnRoomReset(RoomReset room)
        {
            if (state == DroneState.Dormant || state == DroneState.Gone || state == DroneState.Holding || state == DroneState.Exiting)
            {
                return;
            }

            // The chase starts over: the drones wait behind the room's start and have to find the player again.
            PlaceBehindPlayer(resetGap, true);
            if (logDrone)
            {
                Debug.Log($"[{nameof(SecurityDrone)}] '{room.name}' restarted; '{name}' waits {resetGap + slotBehind:F0} behind.", this);
            }
        }

        //==================== SEEING =====================
        private bool SeesPlayer()
        {
            Vector3 from = transform.position;
            Vector3 to = player.transform.position + Vector3.up;
            if (!Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return hit.collider.transform.IsChildOf(player.transform);
        }

        /// <summary>True while any part of the drone is inside the main camera's view.</summary>
        public bool IsVisible()
        {
            Camera view = Camera.main;
            if (view == null)
            {
                return false;
            }

            GeometryUtility.CalculateFrustumPlanes(view, frustum);
            if (renderers.Count == 0)
            {
                return GeometryUtility.TestPlanesAABB(frustum, new Bounds(transform.position, Vector3.one));
            }

            foreach (Renderer r in renderers)
            {
                if (r != null && r.enabled && GeometryUtility.TestPlanesAABB(frustum, r.bounds))
                {
                    return true;
                }
            }

            return false;
        }

        //==================== LOOK =====================
        private void Place(float dt)
        {
            if (route == null)
            {
                return;
            }

            bobPhase += dt * 2.4f;
            Vector3 at = route.PointAt(routeDistance) + Vector3.up * (heightOffset + Mathf.Sin(bobPhase) * bob - reach);
            transform.position = at;
            if (model != null)
            {
                Vector3 heading = route.DirectionAt(routeDistance);
                float side = Mathf.Abs(heading.x) > 0.2f ? Mathf.Sign(heading.x) : (player != null ? Mathf.Sign(player.transform.position.x - at.x + 0.001f) : 1f);
                Quaternion wanted = Quaternion.Euler(heading.y > 0.7f ? -20f : 8f, side > 0f ? 90f : -90f, 0f);
                model.rotation = dt > 0f ? Quaternion.Slerp(model.rotation, wanted, 1f - Mathf.Exp(-6f * dt)) : wanted;
            }
        }

        private void SweepLight()
        {
            if (searchLight == null)
            {
                return;
            }

            float swing = Mathf.Sin(Time.time * 1.3f + bobPhase) * 35f;
            searchLight.transform.rotation = Quaternion.Euler(60f + swing * 0.4f, 90f + swing, 0f);
        }

        private void AimLight()
        {
            if (searchLight == null || player == null)
            {
                return;
            }

            Vector3 to = player.transform.position + Vector3.up - searchLight.transform.position;
            if (to.sqrMagnitude > 0.01f)
            {
                searchLight.transform.rotation = Quaternion.Slerp(searchLight.transform.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-8f * Time.deltaTime));
            }
        }

        private void SetLight(bool on)
        {
            if (searchLight != null)
            {
                searchLight.enabled = on;
            }
        }

        private void SetState(DroneState next)
        {
            if (logDrone && next != state)
            {
                Debug.Log($"[{nameof(SecurityDrone)}] '{name}' {state} -> {next} at {routeDistance:F1} (player {playerDistance:F1}).", this);
            }

            state = next;
            stateSeconds = 0f;
            captureTimer = 0f;
        }
    }
}
