using Alco.Audio;
using Alco.GUI;

namespace Alco.Engine
{
    /// <summary>
    /// Implementation of IUISoundPlayer for playing UI sounds.
    /// </summary>
    public class UISoundPlayer : IUISoundPlayer
    {
        private readonly AudioSource _audioSource;

        /// <summary>
        /// The audio clip to play when a UI element is hovered.
        /// </summary>
        public AudioClip? SoundOnHover { get; set; }

        /// <summary>
        /// The audio clip to play when a UI element is clicked.
        /// </summary>
        public AudioClip? SoundOnClick { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="UISoundPlayer"/> class.
        /// </summary>
        /// <param name="audioDevice">The audio device to create the audio source from.</param>
        public UISoundPlayer(AudioDevice audioDevice)
        {
            _audioSource = audioDevice.CreateAudioSource();
            _audioSource.IsSpatial = false;
        }

        /// <summary>
        /// Plays the hover sound if it is set.
        /// </summary>
        public void PlayOnHoverSound()
        {
            if (SoundOnHover != null)
            {
                _audioSource.AudioClip = SoundOnHover;
                _audioSource.Play();
            }
        }

        /// <summary>
        /// Plays the click sound if it is set.
        /// </summary>
        public void PlayOnClickSound()
        {
            if (SoundOnClick != null)
            {
                _audioSource.AudioClip = SoundOnClick;
                _audioSource.Play();
            }
        }
    }
}

