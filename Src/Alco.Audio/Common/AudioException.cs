namespace Alco.Audio;

/// <summary>
/// The exception thrown when audio decoding or playback setup fails.
/// </summary>
public class AudioException : Exception
{
    /// <summary>
    /// Initializes a new instance with the given error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AudioException(string message) : base(message)
    {

    }
}