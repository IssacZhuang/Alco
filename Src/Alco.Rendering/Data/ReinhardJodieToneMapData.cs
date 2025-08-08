namespace Alco.Rendering;

/// <summary>
/// Parameters for Reinhard-Jodie luminance-preserving tone mapping.
/// </summary>
public struct ReinhardJodieToneMapData
{
    public float Exposure;
    public float Gamma;

    public static readonly ReinhardJodieToneMapData Default = new ReinhardJodieToneMapData
    {
        Exposure = 1.0f,
        Gamma = 2.2f
    };
}

