using Alco.Audio;

namespace Alco.Engine;

/// <summary>
/// The setting for audio interface
/// </summary>
public struct AudioSetting
{
    /// <summary>
    /// The audio backend
    /// </summary>
    public AudioBackend Backend { get; set; }

    /// <summary>
    /// The default audio setting
    /// </summary>
    public static readonly AudioSetting Default = new AudioSetting
    {
        Backend = AudioBackend.Auto,
    };

    /// <summary>
    /// The audio setting for no audio interface
    /// </summary>
    public static readonly AudioSetting NoAudio = new AudioSetting
    {
        Backend = AudioBackend.None,
    };
}
