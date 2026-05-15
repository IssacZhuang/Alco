#include "Shaders/Libs/Core.hlsli"

DEFINE_TEX2D_SAMPLE(0, _texture);
DEFINE_UNIFORM(1, _data)
{
    float Brightness;
    float Contrast;
    float Saturation;
    float HueShift;
    float Temperature;
    float Tint;
    float LiftR;
    float LiftG;
    float LiftB;
    float GammaR;
    float GammaG;
    float GammaB;
    float GainR;
    float GainG;
    float GainB;
    float ShadowR;
    float ShadowG;
    float ShadowB;
    float ShadowStart;
    float HighlightR;
    float HighlightG;
    float HighlightB;
    float HighlightStart;
    float SplitBlend;
};

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

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET
{
    float3 color = SAMPLE_TEX2D(_texture, input.uv).rgb;

    // 1. Color wheels: Lift / Gamma / Gain
    // Lift: additive offset (shadows)
    color += float3(LiftR, LiftG, LiftB);
    // Gamma: power curve (midtones), applied as exp2 offset
    color *= float3(
        exp2(GammaR),
        exp2(GammaG),
        exp2(GammaB)
    );
    // Gain: multiplicative (highlights)
    color *= float3(1.0 + GainR, 1.0 + GainG, 1.0 + GainB);

    // 2. Brightness (additive)
    color += Brightness;

    // 3. Contrast (pivot around 0.5)
    color = (color - 0.5) * (1.0 + Contrast) + 0.5;

    // 4. Saturation
    float lum = Luminance(color);
    color = lerp(float3(lum, lum, lum), color, 1.0 + Saturation);

    // 5. Hue shift (in HSV space)
    if (HueShift != 0.0)
    {
        float3 hsv = RGBtoHSV(color);
        hsv.x = frac(hsv.x + HueShift / 360.0);
        color = HSVtoRGB(hsv);
    }

    // 6. Temperature / Tint (warm-cool shift)
    if (Temperature != 0.0)
    {
        // Warm: boost R, reduce B. Cool: boost B, reduce R.
        color.r += Temperature * 0.1;
        color.b -= Temperature * 0.1;
    }
    if (Tint != 0.0)
    {
        // Tint: magenta-green axis
        color.g += Tint * 0.1;
        color.r -= Tint * 0.05;
        color.b -= Tint * 0.05;
    }

    // 7. Split toning
    if (SplitBlend > 0.0)
    {
        float lum2 = Luminance(color);
        float3 shadowTint = float3(ShadowR, ShadowG, ShadowB);
        float3 highlightTint = float3(HighlightR, HighlightG, HighlightB);

        // Smooth blend based on luminance
        float shadowWeight = 1.0 - smoothstep(ShadowStart - 0.2, ShadowStart + 0.2, lum2);
        float highlightWeight = smoothstep(HighlightStart - 0.2, HighlightStart + 0.2, lum2);

        color += shadowTint * shadowWeight * SplitBlend;
        color += highlightTint * highlightWeight * SplitBlend;
    }

    return float4(color, 1.0);
}
