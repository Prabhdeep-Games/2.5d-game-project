using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Puts the AudioSource on this object in a volume group (Sound Effects, Ambience or Music), so the matching slider
    /// in Settings changes it. The source plays at Base Volume times the group's volume. Master volume is applied to
    /// the whole game through the AudioListener.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioChannel : MonoBehaviour
    {
        private static readonly List<AudioChannel> Active = new List<AudioChannel>();

        [Tooltip("The settings slider this sound follows.")]
        [SerializeField] private AudioChannelKind channel = AudioChannelKind.Effects;

        [Tooltip("Volume of this source when its group is at full volume.")]
        [SerializeField, Range(0f, 1f)] private float baseVolume = 1f;

        private AudioSource source;

        public AudioChannelKind Channel => channel;

        public AudioSource Source => source != null ? source : source = GetComponent<AudioSource>();

        /// <summary>Base volume, for fading a source in or out without losing the setting.</summary>
        public float BaseVolume
        {
            get => baseVolume;
            set
            {
                baseVolume = Mathf.Clamp01(value);
                Refresh();
            }
        }

        public static void RefreshAll()
        {
            foreach (AudioChannel item in Active)
            {
                item.Refresh();
            }
        }

        private void OnEnable()
        {
            Active.Add(this);
            Refresh();
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                Refresh();
            }
        }

        public void Refresh()
        {
            Source.volume = baseVolume * GameSettings.Volume(channel);
        }
    }
}
