namespace Alco.Rendering;

/// <summary>
/// Data parameters for the Uncharted 2 tone mapping operator.
/// </summary>
public struct Uncharted2TonemapData
{
    /// <summary>
    /// Filmic curve parameter A.
    /// </summary>
    public float A;
    /// <summary>
    /// Filmic curve parameter B.
    /// </summary>
    public float B;
    /// <summary>
    /// Filmic curve parameter C.
    /// </summary>
    public float C;
    /// <summary>
    /// Filmic curve parameter D.
    /// </summary>
    public float D;
    /// <summary>
    /// Filmic curve parameter E.
    /// </summary>
    public float E;
    /// <summary>
    /// Filmic curve parameter F.
    /// </summary>
    public float F;
    /// <summary>
    /// White point used for white scaling.
    /// </summary>
    public float W;
    /// <summary>
    /// Exposure multiplier applied before tone mapping.
    /// </summary>
    public float Exposure;
    /// <summary>
    /// Gamma value for the final gamma correction.
    /// </summary>
    public float Gamma;

    /// <summary>
    /// Default Uncharted 2 tone mapping parameters.
    /// </summary>
    public static readonly Uncharted2TonemapData Default = new Uncharted2TonemapData
    {
        A = 0.15f,
        B = 0.50f,
        C = 0.10f,
        D = 0.20f,
        E = 0.02f,
        F = 0.30f,
        W = 11.2f,
        Exposure = 2.0f,
        Gamma = 1f,
    };
}

