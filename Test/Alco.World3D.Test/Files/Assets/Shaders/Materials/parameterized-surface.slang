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
// x = time, y = deltaTime, z = sinTime, w = cosTime. Pure UBO blocks come
// first in the surface's set. Unmarked: engine data, not a material parameter.
cbuffer _globalRenderData : register(b0, space2)
{
    float4 time;
}

// Mixed-type parameter block plus the surface's resources, in one block that
// owns the rest of the set: the UBO takes b1 and resources continue after it.
// The material compiler reads each member's type and byte offset from Slang
// reflection and fills the buffer by name.
[MaterialParams]
cbuffer PulseParams : register(b1, space2)
{
    float pulseSpeed;       // pulses per second
    float pulseIntensity;   // peak emissive multiplier
    float3 pulseColor;      // linear pulse color
    float bandFrequency;    // world-space bands per unit (0 = uniform pulse)

    Texture2D<float4> _albedoTexture;
    SamplerState _albedoTextureSampler;
    Texture2D<float4> _normalTexture;
    SamplerState _normalTextureSampler;
    Texture2D<float4> _metallicRoughnessTexture;
    SamplerState _metallicRoughnessTextureSampler;
}

public struct Surface : ISurface
{
    public override float4 GetBaseColor(SurfaceInput input)
    {
        return _albedoTexture.Sample(_albedoTextureSampler, input.uv) * input.baseColorFactor;
    }

    public override float3 GetNormalTS(SurfaceInput input)
    {
        float2 normalXY = _normalTexture.Sample(_normalTextureSampler, input.uv).rg * 2.0 - 1.0;
        return float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    }

    public override float3 GetMetallicRoughnessAO(SurfaceInput input)
    {
        float4 mrTex = _metallicRoughnessTexture.Sample(_metallicRoughnessTextureSampler, input.uv);
        return float3(
            input.metallicRoughnessAO.x * mrTex.b,
            input.metallicRoughnessAO.y * mrTex.g,
            input.metallicRoughnessAO.z);
    }

    public override float3 GetEmissive(SurfaceInput input)
    {
        // Sinusoidal pulse, optionally modulated by world-space bands so the
        // pulse travels across the object (bandFrequency > 0).
        float phase = time.x * pulseSpeed;
        float band = bandFrequency > 0.0
            ? sin((input.worldPos.x + input.worldPos.y + input.worldPos.z) * bandFrequency - phase * 6.2831853)
            : 1.0;
        float pulse = saturate(sin(phase * 6.2831853) * 0.5 + 0.5) * band;
        return pulseColor * pulseIntensity * pulse;
    }
}
