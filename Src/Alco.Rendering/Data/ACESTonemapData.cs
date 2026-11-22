namespace Alco.Rendering;

/// <summary>Parameters for ACES fitted tone mapping used by <see cref="RGNode_Tonemap"/>.</summary>
public struct ACESTonemapData
{
    /// <summary>ACES curve coefficient A (default 2.51).</summary>
    public float A;

    /// <summary>ACES curve coefficient B (default 0.03).</summary>
    public float B;

    /// <summary>ACES curve coefficient C (default 2.43).</summary>
    public float C;

    /// <summary>ACES curve coefficient D (default 0.59).</summary>
    public float D;

    /// <summary>ACES curve coefficient E (default 0.14).</summary>
    public float E;

    /// <summary>Exposure multiplier applied before tone mapping.</summary>
    public float Exposure;

    /// <summary>Gamma value applied after tone mapping.</summary>
    public float Gamma;

    /// <summary>Default parameter set for ACES fitted tone mapping.</summary>
    public static readonly ACESTonemapData Default = new()
    {
        A = 2.51f,
        B = 0.03f,
        C = 2.43f,
        D = 0.59f,
        E = 0.14f,
        Exposure = 1.0f,
        Gamma = 1.0f,
    };
}
