namespace Alco.Rendering;

public struct ReinhardTonemapData
{
    public float MaxLuminance;
    public float Gamma;

    public static readonly ReinhardTonemapData Default = new ReinhardTonemapData
    {
        MaxLuminance = 1f,
        Gamma = 1f
    };
}