namespace Alco.Rendering;

/// <summary>
/// Parameters for the procedural color grading shader.
/// Field order must exactly match the Slang uniform buffer in ColorGrading.slang.
/// All defaults produce identity (no-op) transformation.
/// </summary>
public struct ColorGradingData
{
    // Basic adjustments
    public float Brightness;
    public float Contrast;
    public float Saturation;
    public float HueShift;

    // Color temperature
    public float Temperature;
    public float Tint;

    // Color wheels - Lift (shadows offset)
    public float LiftR;
    public float LiftG;
    public float LiftB;

    // Color wheels - Gamma (midtones power)
    public float GammaR;
    public float GammaG;
    public float GammaB;

    // Color wheels - Gain (highlights multiplier)
    public float GainR;
    public float GainG;
    public float GainB;

    // Split toning
    public float ShadowR;
    public float ShadowG;
    public float ShadowB;
    public float ShadowStart;
    public float HighlightR;
    public float HighlightG;
    public float HighlightB;
    public float HighlightStart;
    public float SplitBlend;

    /// <summary>
    /// Default parameter set producing no visual change.
    /// </summary>
    public static readonly ColorGradingData Default = new();

    /// <summary>
    /// Returns true if all parameters are at identity defaults (no-op).
    /// ShadowStart and HighlightStart are excluded because they only matter when colors are non-zero.
    /// </summary>
    public bool IsIdentity =>
        Brightness == 0f && Contrast == 0f && Saturation == 0f && HueShift == 0f &&
        Temperature == 0f && Tint == 0f &&
        LiftR == 0f && LiftG == 0f && LiftB == 0f &&
        GammaR == 0f && GammaG == 0f && GammaB == 0f &&
        GainR == 0f && GainG == 0f && GainB == 0f &&
        ShadowR == 0f && ShadowG == 0f && ShadowB == 0f &&
        HighlightR == 0f && HighlightG == 0f && HighlightB == 0f &&
        SplitBlend == 0f;
}
