namespace Alco.Rendering;

/// <summary>
/// Parameters for ACES fitted tone mapping.
/// </summary>
public struct ACESTonemapData
{
    public float A; // 2.51
    public float B; // 0.03
    public float C; // 2.43
    public float D; // 0.59
    public float E; // 0.14
    public float Exposure;
    public float Gamma;

    public static readonly ACESTonemapData Default = new ACESTonemapData
    {
        A = 2.51f,
        B = 0.03f,
        C = 2.43f,
        D = 0.59f,
        E = 0.14f,
        Exposure = 1.0f,
        Gamma = 1f
    };
}

