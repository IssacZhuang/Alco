namespace Vocore.Rendering;

public struct ReinhardToneMapData
{
    public float MaxLuminance;
    public float Gamma;

    public static readonly ReinhardToneMapData Default = new ReinhardToneMapData
    {
        MaxLuminance = 1f,
        Gamma = 1f
    };
}