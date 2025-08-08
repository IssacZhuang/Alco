namespace Alco.Rendering;

/// <summary>
/// Parameters for ACES fitted tone mapping.
/// </summary>
public struct ACESToneMapData
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
    public static readonly ACESToneMapData Default = new ACESToneMapData
    {
        Exposure = 1.0f,
        Gamma = 2.2f,
    };
}

