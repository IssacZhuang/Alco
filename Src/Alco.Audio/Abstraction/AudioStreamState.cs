namespace Alco.Audio;

/// <summary>
/// The logical playback state of an <see cref="AudioStream"/>.
/// </summary>
public enum AudioStreamState
{
    /// <summary>Not playing; the source (if any) has been released and the provider rewound.</summary>
    Stopped = 0,

    /// <summary>Actively playing; buffers are being refilled each frame.</summary>
    Playing = 1,

    /// <summary>Playback paused but the source and queued buffers are retained.</summary>
    Paused = 2,
}
