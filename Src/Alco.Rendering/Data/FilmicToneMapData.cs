namespace Alco.Rendering;

/// <summary>
/// Data for generic filmic curve (Narkowicz-like) used in FilmicTonemap.hlsl.
/// </summary>
public struct FilmicToneMapData
{
    /// <summary>
    /// Exposure multiplier applied before tone mapping.
    /// </summary>
    public float Exposure;

    /// <summary>
    /// Gamma value for final gamma correction.
    /// </summary>
    public float Gamma;

    /// <summary>
    /// Default ACES parameters.
    /// </summary>
    public static readonly FilmicToneMapData Default = new FilmicToneMapData
    {
        Exposure = 1.0f,
        Gamma = 1f,
    };

}

