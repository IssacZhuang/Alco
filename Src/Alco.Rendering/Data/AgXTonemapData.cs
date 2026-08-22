namespace Alco.Rendering;

/// <summary>
/// Parameters for the AgX tone mapping operator used by <c>AgXTonemap.slang</c>.
/// Field order must match the Slang uniform buffer: Exposure, Gamma, Look.
/// Based on the minimal AgX implementation by Benjamin Wrensch (Iolite Engine).
/// </summary>
public struct AgXTonemapData
{
    /// <summary>
    /// Exposure multiplier applied before tone mapping.
    /// </summary>
    public float Exposure;

    /// <summary>
    /// Gamma value used for the final display encoding pass. Default: 2.2 (sRGB).
    /// </summary>
    public float Gamma;

    /// <summary>
    /// Creative look preset: 0 = Default, 1 = Golden, 2 = Punchy.
    /// </summary>
    public float Look;

    /// <summary>
    /// Default parameter set tuned for sRGB output.
    /// </summary>
    public static readonly AgXTonemapData Default = new AgXTonemapData
    {
        Exposure = 1.0f,
        Gamma = 2.2f,
        Look = 0f,
    };
}
