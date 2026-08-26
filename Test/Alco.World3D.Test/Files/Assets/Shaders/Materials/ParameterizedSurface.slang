// module "parameterized_surface"
//
// Test-only surface for exercising the native Slang material-parameter path:
// the standard glTF surface plus a sinusoidal emissive pulse. Unlike the HLSL
// surface contract (bare float4 members only, one register each, parsed by regex),
// the [MaterialParams] block here mixes scalar and vector types with natural HLSL
// packing — and is named freely (the marker, not the name, makes it a parameter
// block). The material compiler reads each member's type and byte offset from
// Slang reflection and fills the buffer by name.

#language slang 2025
module ParameterizedSurface;

import AlcoRendering_Core; // [MaterialParams]
import AlcoWorld3D_Surface;

// The engine binds the per-frame global render data buffer to every material;
// x = time, y = deltaTime, z = sinTime, w = cosTime. Comes first in the
// surface's set. Unmarked: engine data, not a material parameter.
public struct GlobalRenderDataParams
{
    public float4 time;
};

public ParameterBlock<GlobalRenderDataParams> globalRenderData;

// Mixed-type parameter block plus the surface's resources, in one block that
// owns the rest of the set. The material compiler reads each member's type
// and byte offset from Slang reflection and fills the buffer by name.
public struct PulseParamsData
{
    public float pulseSpeed;       // pulses per second
    public float pulseIntensity;   // peak emissive multiplier
    public float3 pulseColor;      // linear pulse color
    public float bandFrequency;    // world-space bands per unit (0 = uniform pulse)

    public Texture2D<float4> albedoTexture;
    public Texture2D<float4> normalTexture;
    public Texture2D<float4> metallicRoughnessTexture;
};

[MaterialParams]
public ParameterBlock<PulseParamsData> pulseParams;

public struct Surface : ISurface
{
    public override float4 getBaseColor(SurfaceInput input)
    {
        return pulseParams.albedoTexture.Sample(samplers.linearRepeat, input.uv) * input.baseColorFactor;
    }

    public override float3 getNormalTS(SurfaceInput input)
    {
        float2 normalXY = pulseParams.normalTexture.Sample(samplers.linearRepeat, input.uv).rg * 2.0 - 1.0;
        return float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    }

    public override float3 getMetallicRoughnessAO(SurfaceInput input)
    {
        float4 mrTex = pulseParams.metallicRoughnessTexture.Sample(samplers.linearRepeat, input.uv);
        return float3(
            input.metallicRoughnessAO.x * mrTex.b,
            input.metallicRoughnessAO.y * mrTex.g,
            input.metallicRoughnessAO.z);
    }

    public override float3 getEmissive(SurfaceInput input)
    {
        // Sinusoidal pulse, optionally modulated by world-space bands so the
        // pulse travels across the object (bandFrequency > 0).
        float phase = globalRenderData.time.x * pulseParams.pulseSpeed;
        float band = pulseParams.bandFrequency > 0.0
            ? sin((input.worldPos.x + input.worldPos.y + input.worldPos.z) * pulseParams.bandFrequency - phase * 6.2831853)
            : 1.0;
        float pulse = saturate(sin(phase * 6.2831853) * 0.5 + 0.5) * band;
        return pulseParams.pulseColor * pulseParams.pulseIntensity * pulse;
    }
}
