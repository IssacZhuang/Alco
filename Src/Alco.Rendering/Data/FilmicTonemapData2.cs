namespace Alco.Rendering;

/// <summary>
/// Data for generic filmic curve (Narkowicz-like) used in FilmicTonemap.hlsl.
/// </summary>
public struct FilmicTonemapData
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
    public static readonly FilmicTonemapData Default = new FilmicTonemapData
    {
        Exposure = 1.0f,
        Gamma = 1f,
    };

}

