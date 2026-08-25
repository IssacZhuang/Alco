// file "color-grading.slang" - module "color_grading"
//
// Slang module converted from Shaders/Pipelines/PostProcess/ColorGrading.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module color_grading;

import alco_rendering_core;

public struct PassParams
{
    public Texture2D _texture;
};

public ParameterBlock<PassParams> _pass;
public struct DataParams
{
    public float Brightness;
    public float Contrast;
    public float Saturation;
    public float HueShift;
    public float Temperature;
    public float Tint;
    public float LiftR;
    public float LiftG;
    public float LiftB;
    public float GammaR;
    public float GammaG;
    public float GammaB;
    public float GainR;
    public float GainG;
    public float GainB;
    public float ShadowR;
    public float ShadowG;
    public float ShadowB;
    public float ShadowStart;
    public float HighlightR;
    public float HighlightG;
    public float HighlightB;
    public float HighlightStart;
    public float SplitBlend;
};

public ParameterBlock<DataParams> _data;


struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

// RGB to HSV conversion
float3 RGBtoHSV(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

// HSV to RGB conversion
float3 HSVtoRGB(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

// Luminance (Rec. 709)
float Luminance(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F o = (V2F)0;
    o.position = float4(input.position, 1);
    o.uv = input.uv;
    return o;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET
{
    float3 color = _pass._texture.Sample(_samplers._linearClamp, input.uv).rgb;

    // 1. Color wheels: Lift / Gamma / Gain
    // Lift: additive offset (shadows)
    color += float3(_data.LiftR, _data.LiftG, _data.LiftB);
    // Gamma: power curve (midtones), applied as exp2 offset
    color *= float3(
        exp2(_data.GammaR),
        exp2(_data.GammaG),
        exp2(_data.GammaB)
    );
    // Gain: multiplicative (highlights)
    color *= float3(1.0 + _data.GainR, 1.0 + _data.GainG, 1.0 + _data.GainB);

    // 2. Brightness (additive)
    color += _data.Brightness;

    // 3. Contrast (pivot around 0.5)
    color = (color - 0.5) * (1.0 + _data.Contrast) + 0.5;

    // 4. Saturation
    float lum = Luminance(color);
    color = lerp(float3(lum, lum, lum), color, 1.0 + _data.Saturation);

    // 5. Hue shift (in HSV space)
    if (_data.HueShift != 0.0)
    {
        float3 hsv = RGBtoHSV(color);
        hsv.x = frac(hsv.x + _data.HueShift / 360.0);
        color = HSVtoRGB(hsv);
    }

    // 6. Temperature / Tint (warm-cool shift)
    if (_data.Temperature != 0.0)
    {
        // Warm: boost R, reduce B. Cool: boost B, reduce R.
        color.r += _data.Temperature * 0.1;
        color.b -= _data.Temperature * 0.1;
    }
    if (_data.Tint != 0.0)
    {
        // Tint: magenta-green axis
        color.g += _data.Tint * 0.1;
        color.r -= _data.Tint * 0.05;
        color.b -= _data.Tint * 0.05;
    }

    // 7. Split toning
    if (_data.SplitBlend > 0.0)
    {
        float lum2 = Luminance(color);
        float3 shadowTint = float3(_data.ShadowR, _data.ShadowG, _data.ShadowB);
        float3 highlightTint = float3(_data.HighlightR, _data.HighlightG, _data.HighlightB);

        // Smooth blend based on luminance
        float shadowWeight = 1.0 - smoothstep(_data.ShadowStart - 0.2, _data.ShadowStart + 0.2, lum2);
        float highlightWeight = smoothstep(_data.HighlightStart - 0.2, _data.HighlightStart + 0.2, lum2);

        color += shadowTint * shadowWeight * _data.SplitBlend;
        color += highlightTint * highlightWeight * _data.SplitBlend;
    }

    return float4(color, 1.0);
}
