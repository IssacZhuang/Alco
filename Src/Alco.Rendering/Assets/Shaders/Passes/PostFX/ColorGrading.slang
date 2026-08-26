// file "ColorGrading.slang" - module "ColorGrading"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module ColorGrading;

import AlcoRendering_Core;

public struct PassParams
{
    public Texture2D texture;
};

public ParameterBlock<PassParams> pass;
public struct DataParams
{
    public float brightness;
    public float contrast;
    public float saturation;
    public float hueShift;
    public float temperature;
    public float tint;
    public float liftR;
    public float liftG;
    public float liftB;
    public float gammaR;
    public float gammaG;
    public float gammaB;
    public float gainR;
    public float gainG;
    public float gainB;
    public float shadowR;
    public float shadowG;
    public float shadowB;
    public float shadowStart;
    public float highlightR;
    public float highlightG;
    public float highlightB;
    public float highlightStart;
    public float splitBlend;
};

public ParameterBlock<DataParams> data;


struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

// rgb to HSV conversion
float3 rGBtoHSV(float3 c)
{
    float4 k = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, k.wz), float4(c.gb, k.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

// HSV to rgb conversion
float3 hSVtoRGB(float3 c)
{
    float4 k = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + k.xyz) * 6.0 - k.www);
    return c.z * lerp(k.xxx, clamp(p - k.xxx, 0.0, 1.0), c.y);
}

// luminance (Rec. 709)
float luminance(float3 c)
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
    float3 color = pass.texture.Sample(samplers.linearClamp, input.uv).rgb;

    // 1. color wheels: Lift / gamma / Gain
    // Lift: additive offset (shadows)
    color += float3(data.liftR, data.liftG, data.liftB);
    // gamma: power curve (midtones), applied as exp2 offset
    color *= float3(
        exp2(data.gammaR),
        exp2(data.gammaG),
        exp2(data.gammaB)
    );
    // Gain: multiplicative (highlights)
    color *= float3(1.0 + data.gainR, 1.0 + data.gainG, 1.0 + data.gainB);

    // 2. brightness (additive)
    color += data.brightness;

    // 3. contrast (pivot around 0.5)
    color = (color - 0.5) * (1.0 + data.contrast) + 0.5;

    // 4. saturation
    float lum = luminance(color);
    color = lerp(float3(lum, lum, lum), color, 1.0 + data.saturation);

    // 5. Hue shift (in HSV space)
    if (data.hueShift != 0.0)
    {
        float3 hsv = rGBtoHSV(color);
        hsv.x = frac(hsv.x + data.hueShift / 360.0);
        color = hSVtoRGB(hsv);
    }

    // 6. temperature / tint (warm-cool shift)
    if (data.temperature != 0.0)
    {
        // Warm: boost R, reduce B. Cool: boost B, reduce R.
        color.r += data.temperature * 0.1;
        color.b -= data.temperature * 0.1;
    }
    if (data.tint != 0.0)
    {
        // tint: magenta-green axis
        color.g += data.tint * 0.1;
        color.r -= data.tint * 0.05;
        color.b -= data.tint * 0.05;
    }

    // 7. Split toning
    if (data.splitBlend > 0.0)
    {
        float lum2 = luminance(color);
        float3 shadowTint = float3(data.shadowR, data.shadowG, data.shadowB);
        float3 highlightTint = float3(data.highlightR, data.highlightG, data.highlightB);

        // Smooth blend based on luminance
        float shadowWeight = 1.0 - smoothstep(data.shadowStart - 0.2, data.shadowStart + 0.2, lum2);
        float highlightWeight = smoothstep(data.highlightStart - 0.2, data.highlightStart + 0.2, lum2);

        color += shadowTint * shadowWeight * data.splitBlend;
        color += highlightTint * highlightWeight * data.splitBlend;
    }

    return float4(color, 1.0);
}
