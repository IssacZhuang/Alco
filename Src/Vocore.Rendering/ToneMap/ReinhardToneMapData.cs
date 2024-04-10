namespace Vocore.Rendering;

public struct ReinhardToneMapData
{
    public float MaxLuminance;
    public float Exposure;
    public float Gamma;

    public static readonly ReinhardToneMapData Default = new ReinhardToneMapData
    {
        MaxLuminance = 1f,
        Gamma = 1 / 2.2f
    };
}