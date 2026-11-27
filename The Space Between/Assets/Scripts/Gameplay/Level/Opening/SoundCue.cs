using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Plays a sound once, the first time the player's middle enters this object's Box Collider: for example distant
    /// machinery heard behind the player. A source with no clip yet stays silent, so the cue is ready for the final sound.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class SoundCue : MonoBehaviour
    {
        [Tooltip("The sound to play. It can be anywhere in the scene, for example behind the player.")]
        [SerializeField] private AudioSource source;

        [Tooltip("The player. Empty means the object with a PlaceholderPlayerController.")]
        [SerializeField] private Transform player;

        [Header("Debug")]
        [ReadOnly, SerializeField] private bool played;

        private BoxCollider area;

        public bool Played => played;

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
            if (played || player == null || !area.bounds.Contains(player.position + Vector3.up))
            {
                return;
            }

            played = true;
            if (source != null && source.clip != null)
            {
                source.Play();
            }
        }
    }
}
