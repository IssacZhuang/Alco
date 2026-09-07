// file "TrailSurface3DSmoke.slang" - module "TrailSurface3DSmoke"
//
// The 3D smoke trail surface - the 3D counterpart of TrailSurfaceSmoke: the
// trail pass template (GpuTrail3D) supplies the camera-facing ribbon geometry,
// the width envelope and the fade alpha; this surface adds the smoke look on
// top - the vertex hook wanders the ribbon with age-amplified turbulence (along
// the side axis and the lift axis perpendicular to both side and tangent), lets
// the aged body rise buoyantly and bulges/necks its width along the arc
// (two-octave noise); the fragment stage erodes the body with fbm noise as it
// dissipates. Wider (bulged) parts read fainter, necks denser - the density
// conservation compensates the width scale in the fragment alpha.
//
// The look constants are material parameters ([MaterialParams]), authored in
// the .amat's parameter table; the per-trail opacity rides the template's
// envelope alpha (input.color.a), so one material serves every projectile's
// tuned trail.

#language slang 2025
module TrailSurface3DSmoke;

import AlcoRendering_Core; // [MaterialParams]
import AlcoRendering_Trails3D; // TrailSample3D
import AlcoRendering_TrailSurface3D; // ITrailSurface3D, TrailSurface3DInput, TrailVertex3DInput, TrailVertex3DResult

// The smoke look constants (static per material, packed from the .amat's
// parameter table).
public struct SmokeParamsData
{
    public float4 smokeColor; // rgb: color, a: global opacity
    public float4 shape;      // x: noiseScale, y: noiseDrift, z: turbAmp, w: turbFreq
    public float4 width;      // x: widthVar, y: widthFreq
    public float4 edge;       // x: edgeStart, y: edgePow, z: start fade distance (world units, 0 disables)
    public float4 motion;     // x: buoyant rise speed (world units/s at full age), y: rise onset (age01)
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

public struct Surface : ITrailSurface3D
{
    public override TrailVertex3DResult getVertexAdjustment(TrailVertex3DInput input)
    {
        TrailVertex3DResult result;
        result.worldOffset = float3(0.0, 0.0, 0.0);
        result.widthScale = 1.0;
        result.data = float4(input.sample.distanceFromStart, 0.0, 0.0, 0.0);

        float seed = input.sample.trail.misc.x;
        float u = input.sample.u;
        float age01 = input.sample.age01;

        // Non-uniform width along the ribbon (two-octave noise): bulges and
        // necks. The fragment stage compensates alpha by 1/widthScale so
        // narrow parts look denser.
        float widthFreq = smokeParams.width.y;
        float widthVar = smokeParams.width.x;
        float wn = vnoise1(u * widthFreq + seed) * 0.65 + vnoise1(u * widthFreq * 2.7 + seed * 1.7) * 0.35;
        result.widthScale = lerp(1.0 - widthVar, 1.0 + widthVar, wn);
        result.data.w = result.widthScale;

        // Turbulence wander, amplified by age: older smoke wanders further. The
        // wander axes are the camera-facing side and the lift axis perpendicular
        // to both side and tangent (the ribbon's local "up"); two decorrelated
        // noise reads keep the motion off the ribbon plane.
        float3 lift = cross(input.sample.side, input.sample.tangent);
        float turbFreq = smokeParams.shape.w;
        float turbAmp = smokeParams.shape.z;
        float wander = turbAmp * smoothstep(0.0, 1.0, input.sample.age);
        result.worldOffset += input.sample.side * (vnoise1(u * turbFreq + seed) - 0.5) * wander;
        result.worldOffset += lift * (vnoise1(u * turbFreq + seed * 3.1 + 17.0) - 0.5) * wander;

        // Buoyant rise: the aged body drifts up (engine convention: +Z is up),
        // delayed by the rise onset so the fresh head stays on the flight path.
        float rise = smokeParams.motion.x;
        float onset = smokeParams.motion.y;
        if (rise > 0.0)
        {
            float t = smoothstep(onset, 1.0, age01) * input.sample.age;
            result.worldOffset += float3(0.0, 0.0, 1.0) * (rise * t);
        }
        return result;
    }

    public override float4 getColor(TrailSurface3DInput input)
    {
        // Edge gradient: edgeStart shifts where the falloff begins, edgePow
        // shapes the curve.
        float edge = pow(1.0 - smoothstep(smokeParams.edge.x, 1.0, abs(input.uv.y)), smokeParams.edge.y);

        // Soften the start of the retained ribbon, independently of the
        // age envelope. Evaluate per fragment so sparse points keep a smooth ramp.
        float startFade = smokeParams.edge.z <= 0.0 ? 1.0 : smoothstep(0.0, smokeParams.edge.z, input.data.x);

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

        float a = edge * startFade * dens * cut * lerp(0.55, 1.0, m) * smokeParams.smokeColor.a;
        if (a < 0.003) discard;

        // Slightly brighter core fakes volumetric thickness; the trail's
        // color gradient tints on top (identity white by default).
        float3 col = smokeParams.smokeColor.rgb * lerp(0.82, 1.0, edge * m) * input.color.rgb;
        // Premultiplied alpha (the trail pass's blend state).
        return float4(col * a, a);
    }
}
