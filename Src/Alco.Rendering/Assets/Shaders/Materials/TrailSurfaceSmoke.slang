// file "TrailSurfaceSmoke.slang" - module "TrailSurfaceSmoke"
//
// The smoke trail surface (ported from the web prototype
// Docs/Prototypes/bullet_trail.html): the trail pass template (GpuTrail2D)
// supplies the ribbon geometry, the width envelope and the fade alpha; this
// surface adds the smoke look on top - the vertex hook wanders the ribbon
// with age-amplified turbulence and bulges/necks its width along the arc
// (two-octave noise), the fragment stage erodes the body with fbm noise as
// it dissipates. Wider (bulged) parts read fainter, necks denser - the
// density conservation compensates the width scale in the fragment alpha.
//
// The look constants are material parameters ([MaterialParams]), authored in
// the .amat's parameter table; the per-trail opacity rides the template's
// envelope alpha (input.color.a), so one material serves every projectile's
// tuned trail.

#language slang 2025
module TrailSurfaceSmoke;

import AlcoRendering_Core; // [MaterialParams]
import AlcoRendering_TrailSurface; // ITrailSurface, TrailSurfaceInput, TrailVertexInput, TrailVertexResult

// The smoke look constants (static per material, packed from the .amat's
// parameter table).
public struct SmokeParamsData
{
    public float4 smokeColor; // rgb: color, a: global opacity
    public float4 shape;      // x: noiseScale, y: noiseDrift, z: turbAmp, w: turbFreq
    public float4 width;      // x: widthVar, y: widthFreq
    public float4 edge;       // x: edgeStart, y: edgePow
};

[MaterialParams]
public ParameterBlock<SmokeParamsData> smokeParams;

float hash1(float n) { return frac(sin(n) * 43758.5453123); }

float vnoise1(float x)
{
    float i = floor(x), f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(hash1(i), hash1(i + 1.0), f);
}

float hash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123); }

float vnoise2(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(hash2(i), hash2(i + float2(1, 0)), f.x),
                lerp(hash2(i + float2(0, 1)), hash2(i + float2(1, 1)), f.x), f.y);
}

float fbm(float2 p) { return vnoise2(p) * 0.6 + vnoise2(p * 2.3) * 0.3 + vnoise2(p * 5.1) * 0.1; }

public struct Surface : ITrailSurface
{
    public override TrailVertexResult adjustRibbonVertex(TrailVertexInput input)
    {
        TrailVertexResult result;
        result.worldOffset = float2(0.0, 0.0);
        result.widthScale = 1.0;
        result.data = float4(0.0, 0.0, 0.0, 0.0);

        float seed = input.sample.trail.misc.x;
        float u = input.sample.u;

        // Non-uniform width along the ribbon (two-octave noise): bulges and
        // necks. The fragment stage compensates alpha by 1/widthScale so
        // narrow parts look denser.
        float widthFreq = smokeParams.width.y;
        float widthVar = smokeParams.width.x;
        float wn = vnoise1(u * widthFreq + seed) * 0.65 + vnoise1(u * widthFreq * 2.7 + seed * 1.7) * 0.35;
        result.widthScale = lerp(1.0 - widthVar, 1.0 + widthVar, wn);
        result.data.w = result.widthScale;

        // Turbulence wander, amplified by age: older smoke wanders further.
        float turbFreq = smokeParams.shape.w;
        float turbAmp = smokeParams.shape.z;
        result.worldOffset = input.sample.normal
            * (vnoise1(u * turbFreq + seed) - 0.5) * turbAmp * smoothstep(0.0, 1.0, input.sample.age);
        return result;
    }

    public override float4 shade(TrailSurfaceInput input)
    {
        // Edge gradient: edgeStart shifts where the falloff begins, edgePow
        // shapes the curve.
        float edge = pow(1.0 - smoothstep(smokeParams.edge.x, 1.0, abs(input.uv.y)), smokeParams.edge.y);

        // Smoke density field, slowly crawling along the ribbon over time.
        float2 nc = float2(input.uv.x * smokeParams.shape.x - input.time * smokeParams.shape.y,
                           input.uv.y * 0.6 + input.seed);
        float m = fbm(nc);

        // Erosion: as the point ages the threshold rises and the body breaks
        // into wisps (age-relative, like every envelope of the template).
        float erosion = smoothstep(0.3, 0.95, input.age01);
        float cut = clamp((m - erosion * 0.9) / 0.3, 0.0, 1.0);

        // Density conservation: wider (noise-bulged) parts are fainter, necks
        // are denser. input.color.a carries the template's envelope alpha
        // (opacity x fade in/out).
        float dens = input.color.a / max(input.data.w, 0.35);

        float a = edge * dens * cut * lerp(0.55, 1.0, m) * smokeParams.smokeColor.a;
        if (a < 0.003) discard;

        // Slightly brighter core fakes volumetric thickness; the trail's
        // color gradient tints on top (identity white by default).
        float3 col = smokeParams.smokeColor.rgb * lerp(0.82, 1.0, edge * m) * input.color.rgb;
        // Premultiplied alpha (the trail pass's blend state).
        return float4(col * a, a);
    }
}
