namespace Alco.Rendering;

/// <summary>
/// The data structure for Uncharted 2 Tone Mapping.
/// </summary>
public struct U2ToneMapData
{
    public float A;
    public float B;
    public float C;
    public float D;
    public float E;
    public float F;
    public float W;
    public float Exposure;
    public float Gamma;

    public static readonly U2ToneMapData Default = new U2ToneMapData
    {
        A = 0.15f,
        B = 0.50f,
        C = 0.10f,
        D = 0.20f,
        E = 0.02f,
        F = 0.30f,
        W = 11.2f,
        Exposure = 2.0f,
        Gamma = 1
    };
}