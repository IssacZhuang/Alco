namespace Alco.Audio;

/// <summary>
/// Specifies the attenuation mode for audio sources.
/// </summary>
public enum AudioAttenuationMode
{
    /// <summary>
    /// Inverse distance attenuation model (equivalent to InverseDistanceClamped in OpenAL).
    /// </summary>
    Inverse,

    /// <summary>
    /// Linear distance attenuation model (equivalent to LinearDistanceClamped in OpenAL).
    /// </summary>
    Linear,

    /// <summary>
    /// Exponential distance attenuation model (equivalent to ExponentDistanceClamped in OpenAL).
    /// </summary>
    Exponent
}
