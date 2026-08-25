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
module parameterized_surface;

import alco_rendering_core; // [MaterialParams]
import alco_world3d_surface;

// The engine binds the per-frame global render data buffer to every material;
// x = time, y = deltaTime, z = sinTime, w = cosTime. Comes first in the
// surface's set. Unmarked: engine data, not a material parameter.
public struct GlobalRenderDataParams
{
    public float4 time;
};

public ParameterBlock<GlobalRenderDataParams> _globalRenderData;

// Mixed-type parameter block plus the surface's resources, in one block that
// owns the rest of the set. The material compiler reads each member's type
// and byte offset from Slang reflection and fills the buffer by name.
public struct PulseParamsData
{
    public float pulseSpeed;       // pulses per second
    public float pulseIntensity;   // peak emissive multiplier
    public float3 pulseColor;      // linear pulse color
    public float bandFrequency;    // world-space bands per unit (0 = uniform pulse)

    public Texture2D<float4> _albedoTexture;
    public SamplerState _albedoTextureSampler;
    public Texture2D<float4> _normalTexture;
    public SamplerState _normalTextureSampler;
    public Texture2D<float4> _metallicRoughnessTexture;
    public SamplerState _metallicRoughnessTextureSampler;
};

[MaterialParams]
public ParameterBlock<PulseParamsData> PulseParams;

public struct Surface : ISurface
{
    public override float4 GetBaseColor(SurfaceInput input)
    {
        return PulseParams._albedoTexture.Sample(PulseParams._albedoTextureSampler, input.uv) * input.baseColorFactor;
    }

    public override float3 GetNormalTS(SurfaceInput input)
    {
        float2 normalXY = PulseParams._normalTexture.Sample(PulseParams._normalTextureSampler, input.uv).rg * 2.0 - 1.0;
        return float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    }

    public override float3 GetMetallicRoughnessAO(SurfaceInput input)
    {
        float4 mrTex = PulseParams._metallicRoughnessTexture.Sample(PulseParams._metallicRoughnessTextureSampler, input.uv);
        return float3(
            input.metallicRoughnessAO.x * mrTex.b,
            input.metallicRoughnessAO.y * mrTex.g,
            input.metallicRoughnessAO.z);
    }

    public override float3 GetEmissive(SurfaceInput input)
    {
        // Sinusoidal pulse, optionally modulated by world-space bands so the
        // pulse travels across the object (bandFrequency > 0).
        float phase = _globalRenderData.time.x * PulseParams.pulseSpeed;
        float band = PulseParams.bandFrequency > 0.0
            ? sin((input.worldPos.x + input.worldPos.y + input.worldPos.z) * PulseParams.bandFrequency - phase * 6.2831853)
            : 1.0;
        float pulse = saturate(sin(phase * 6.2831853) * 0.5 + 0.5) * band;
        return PulseParams.pulseColor * PulseParams.pulseIntensity * pulse;
    }
}
