using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace TheSpaceBetween
{
    /// <summary>
    /// Puts one puzzle room back the way it started, so a player who has made it unsolvable can try again
    /// instead of being stuck. Swapping can leave the only useful object somewhere it can no longer be
    /// reached, so every puzzle room should have one.
    ///
    /// The room is the Box Collider on this object (a trigger on the Ignore Raycast layer, so nothing collides
    /// with it or aims at it). While the player is inside it, pressing the reset action moves every listed
    /// object back to its starting position and rotation, and the player to the entry point. All of them
    /// lose their momentum.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class RoomReset : MonoBehaviour
    {
        //==================== INPUT =====================
        [Header("Input")]
        [Tooltip("Resets the room the player is in. Reads R and the gamepad Select (View) button.")]
        [SerializeField] private InputAction resetAction = new InputAction("Reset Room", InputActionType.Button);

        //==================== ROOM =====================
        [Header("Room")]
        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private Swappable player;

        [Tooltip("Where the player is put back. Its position is the player's standing point (feet).")]
        [SerializeField] private Transform entryPoint;

        [Tooltip("Objects put back on reset. Empty means every Swappable inside the room box when the scene starts, except the player.")]
        [SerializeField] private List<Swappable> resettables = new List<Swappable>();

        //==================== EVENTS =====================
        [Header("Events")]
        [Tooltip("Invoked after the room has been reset.")]
        [SerializeField] private UnityEvent resetEvent;

        //==================== DEBUG =====================
        [Header("Debug")]
        [Tooltip("Write each reset to the Console.")]
        [SerializeField] private bool logResets;

        [Tooltip("Whether the player is inside this room right now.")]
        [ReadOnly, SerializeField] private bool playerInside;

        private BoxCollider area;
        private readonly List<Pose> startPoses = new List<Pose>();

        public bool PlayerInside => playerInside;

        //==================== LIFECYCLE =====================
        private void Reset()
        {
            resetAction = new InputAction("Reset Room", InputActionType.Button, "<Keyboard>/r");
            resetAction.AddBinding("<Gamepad>/select");

            area = GetComponent<BoxCollider>();
            area.isTrigger = true;
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        private void Awake()
        {
            area = GetComponent<BoxCollider>();

            if (player == null)
            {
                PlaceholderPlayerController controller = FindAnyObjectByType<PlaceholderPlayerController>();
                player = controller != null ? controller.GetComponent<Swappable>() : null;
            }

            if (player == null)
            {
                Debug.LogWarning($"[{nameof(RoomReset)}] '{name}' found no player. Assign one.", this);
            }
        }

        private void Start()
        {
            // Collected in Start, so every Swappable has registered itself first.
            if (resettables.Count == 0)
            {
                Bounds room = area.bounds;
                foreach (Swappable candidate in Swappable.Active)
                {
                    if (candidate != player && room.Contains(candidate.Center))
                    {
                        resettables.Add(candidate);
                    }
                }
            }

            startPoses.Clear();
            foreach (Swappable item in resettables)
            {
                startPoses.Add(item != null ? new Pose(item.Position, item.transform.rotation) : Pose.identity);
            }
        }

        private void OnEnable()
        {
            resetAction.performed += OnResetPerformed;
            resetAction.Enable();
        }

        private void OnDisable()
        {
            resetAction.performed -= OnResetPerformed;
            resetAction.Disable();
        }

        private void Update()
        {
            playerInside = player != null && area.bounds.Contains(player.Center);
        }

        //==================== RESET =====================
        private void OnResetPerformed(InputAction.CallbackContext context)
        {
            if (player != null && area.bounds.Contains(player.Center))
            {
                ResetRoom();
            }
        }

        /// <summary>Moves every listed object back to its start and the player to the entry point.</summary>
        public void ResetRoom()
        {
            // Collider bounds lag behind a transform moved this frame, and the player's standing point is read from them.
            Physics.SyncTransforms();

            for (int i = 0; i < resettables.Count; i++)
            {
                Swappable item = resettables[i];
                if (item == null || item.IsFrozen)
                {
                    continue;
                }

                item.transform.rotation = startPoses[i].rotation;
                item.MoveTo(startPoses[i].position);
            }

            if (player != null && entryPoint != null && !player.IsFrozen)
            {
                // The entry point marks the feet, and MoveTo places the pivot, so the difference is added.
                Vector3 pivotAboveFeet = player.Position - player.Anchor;
                player.MoveTo(entryPoint.position + pivotAboveFeet);
            }

            Physics.SyncTransforms();
            resetEvent?.Invoke();

            if (logResets)
            {
                Debug.Log($"[{nameof(RoomReset)}] '{name}' reset {resettables.Count} objects and the player.", this);
            }
        }

        //==================== GIZMOS =====================
        private void OnDrawGizmos()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;

            if (entryPoint != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(entryPoint.position, 0.25f);
            }
        }
    }
}
