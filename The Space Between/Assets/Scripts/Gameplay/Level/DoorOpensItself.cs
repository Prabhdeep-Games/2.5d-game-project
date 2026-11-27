using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// A door that opens on its own as the player approaches: nobody pressed anything, so someone let them through.
    /// When the player's middle enters the Box Collider, the linked mechanisms are powered after a short pause, once.
    /// The collider should be a trigger on the Ignore Raycast layer, a few metres before the door.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class DoorOpensItself : MonoBehaviour
    {
        [Tooltip("The doors (or other mechanisms) powered when the player arrives.")]
        [SerializeField] private List<Mechanism> targets = new List<Mechanism>();

        [Tooltip("Seconds between the player arriving and the door opening, so it reads as a decision made elsewhere.")]
        [SerializeField, Min(0f)] private float delaySeconds = 1.2f;

        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private Transform player;

        [ReadOnly, SerializeField] private bool opened;

        private BoxCollider area;
        private float arrivedAt = -1f;

        public bool Opened => opened;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        }

        private void Awake()
        {
            area = GetComponent<BoxCollider>();
            if (player == null)
            {
                PlaceholderPlayerController controller = FindAnyObjectByType<PlaceholderPlayerController>();
                player = controller != null ? controller.transform : null;
            }
        }

        private void Update()
        {
            if (opened || player == null)
            {
                return;
            }

            if (arrivedAt < 0f)
            {
                if (area.bounds.Contains(player.position + Vector3.up))
                {
                    arrivedAt = Time.time;
                }

                return;
            }

            if (Time.time - arrivedAt < delaySeconds)
            {
                return;
            }

            opened = true;
            foreach (Mechanism target in targets)
            {
                if (target != null)
                {
                    target.SetPowered(true);
                }
            }
        }
    }
}
