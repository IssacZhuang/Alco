namespace Alco.Rendering;

/// <summary>Parameters for Khronos PBR Neutral tone mapping used by <see cref="RGNode_Tonemap"/>.</summary>
public struct NeutralTonemapData
{
    /// <summary>Exposure multiplier applied before tone mapping.</summary>
    public float Exposure;

    /// <summary>Gamma value used for the final gamma correction pass.</summary>
    public float Gamma;

    /// <summary>Start of compression region (end of linear region).</summary>
    public float StartCompression;

    /// <summary>Desaturation factor within the compression region.</summary>
    public float Desaturation;

    /// <summary>Default parameter set tuned for HDR output.</summary>
    public static readonly NeutralTonemapData Default = new()
    {
        Exposure = 1.0f,
        Gamma = 1.0f,
        StartCompression = 0.9f,
        Desaturation = 2.0f,
    };
}
