namespace Alco.Rendering;

/// <summary>
/// Parameters for the Khronos PBR Neutral tone mapping operator used by <c>NeutralTonemap.hlsl</c>.
/// Field order must match the HLSL uniform buffer: Exposure, Gamma, StartCompression, Desaturation.
/// </summary>
public struct NeutralToneMapData
{
    /// <summary>
    /// Exposure multiplier applied before tone mapping.
    /// </summary>
    public float Exposure;

    /// <summary>
    /// Gamma value used for the final gamma correction pass.
    /// </summary>
    public float Gamma;

    /// <summary>
    /// Start of compression region (end of linear region). Typical default: 0.76 (0.8 - 0.04).
    /// </summary>
    public float StartCompression;

    /// <summary>
    /// Desaturation factor within the compression region. Typical default: 0.15.
    /// </summary>
    public float Desaturation;

    /// <summary>
    /// Default parameter set tuned for SDR sRGB output.
    /// </summary>
    public static readonly NeutralToneMapData Default = new NeutralToneMapData
    {
        Exposure = 1.0f,
        Gamma = 1.0f,
        StartCompression = 0.76f,
        Desaturation = 0.15f,
    };
}

