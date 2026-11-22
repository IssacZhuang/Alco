// file "TrailSurfaceSmoke.slang" - module "TrailSurfaceSmoke"
//
// Shared 2D/3D volumetric smoke trail surface with turbulence wander, buoyant
// rise, noise width bulges, density conservation, and fbm erosion.

#language slang 2025
module TrailSurfaceSmoke;

import Core;
import AlcoRendering_TrailNoise;
import AlcoRendering_TrailSurface;

public struct SmokeParamsData
{
    public float4 smokeColor; // rgb: color, a: global opacity
    public float4 shape;      // x: noiseScale, y: noiseDrift, z: turbAmp, w: turbFreq
    public float4 width;      // x: widthVar, y: widthFreq
    public float4 edge;       // x: edgeStart, y: edgePow, z: start fade distance
    public float4 motion;     // x: buoyant rise speed, y: rise onset (age01)
};

float hash1(float n) { return frac(sin(n) * 43758.5453123); }

float vnoise1(float x)
{
    float i = floor(x), f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(hash1(i), hash1(i + 1.0), f);
}

public struct Surface : ITrailSurface
{
    public typedef SmokeParamsData Params;

    public override TrailVertexResult getVertexAdjustment(TrailVertexInput input, Params p)
    {
        TrailVertexResult result;
        result.worldOffset = float3(0.0, 0.0, 0.0);
        result.widthScale = 1.0;
        result.data = float4(input.distanceFromStart, 0.0, 0.0, 0.0);

        float seed = input.seed;
        float u = input.uv.x;
        float age01 = input.age01;

        float widthFreq = p.width.y;
        float widthVar = p.width.x;
        float wn = vnoise1(u * widthFreq + seed) * 0.65 + vnoise1(u * widthFreq * 2.7 + seed * 1.7) * 0.35;
        result.widthScale = lerp(1.0 - widthVar, 1.0 + widthVar, wn);
        result.data.w = result.widthScale;

        float3 lift = cross(input.side, input.tangent);
        float turbFreq = p.shape.w;
        float turbAmp = p.shape.z;
        float wander = turbAmp * smoothstep(0.0, 1.0, input.age);
        result.worldOffset += input.side * (vnoise1(u * turbFreq + seed) - 0.5) * wander;
        result.worldOffset += lift * (vnoise1(u * turbFreq + seed * 3.1 + 17.0) - 0.5) * wander;

        float rise = p.motion.x;
        float onset = p.motion.y;
        if (rise > 0.0)
        {
            float t = smoothstep(onset, 1.0, age01) * input.age;
            result.worldOffset += input.upDirection * (rise * t);
        }
        return result;
    }

    public override float4 getColor(TrailSurfaceInput input, Params p)
    {
        float edge = pow(1.0 - smoothstep(p.edge.x, 1.0, abs(input.uv.y)), p.edge.y);
        float startFade = p.edge.z <= 0.0 ? 1.0 : smoothstep(0.0, p.edge.z, input.data.x);

        float2 nc = float2(input.uv.x * p.shape.x - input.time * p.shape.y,
                           input.uv.y * 0.6 + input.seed);
        float m = TrailFbm2D(nc);

        float erosion = smoothstep(0.3, 0.95, input.age01);
        float cut = clamp((m - erosion * 0.9) / 0.3, 0.0, 1.0);
        float dens = input.color.a / max(input.data.w, 0.35);

        float a = edge * startFade * dens * cut * lerp(0.55, 1.0, m) * p.smokeColor.a;
        if (a < 0.003) discard;

        float3 col = p.smokeColor.rgb * lerp(0.82, 1.0, edge * m) * input.color.rgb;
        return float4(col * a, a);
    }
}
