// module "parameterized_surface"
//
// Test-only surface for exercising the native Slang material-parameter path:
// the standard glTF surface plus a sinusoidal emissive pulse. Unlike the HLSL
// surface contract (bare float4 members only, one register each, parsed by regex),
// the _materialParams block here mixes scalar and vector types with natural HLSL
// packing. The material compiler reads each member's type and byte offset from
// Slang reflection and fills the buffer by name.

#language slang 2025
module parameterized_surface;

import surface;

// Mixed-type parameter block: reflection reports pulseSpeed/pulseIntensity at
// offset 0/4, pulseColor at 16, bandFrequency at 28 - packing the material
// compiler does not need to know, it just writes floats at the reflected
// offsets.
[[vk::binding(0, 2)]] cbuffer _materialParams
{
    float pulseSpeed;       // pulses per second
    float pulseIntensity;   // peak emissive multiplier
    float3 pulseColor;      // linear pulse color
    float bandFrequency;    // world-space bands per unit (0 = uniform pulse)
}

// The engine binds the per-frame global render data buffer to every material;
// x = time, y = deltaTime, z = sinTime, w = cosTime.
[[vk::binding(1, 2)]] cbuffer _globalRenderData
{
    float4 time;
}

// Surface resources live in set 2 (the surface contract): explicit, stable
// across pass permutations and define permutations alike.
[[vk::binding(2, 2)]] Texture2D<float4> _albedoTexture;
[[vk::binding(3, 2)]] SamplerState _albedoTextureSampler;
[[vk::binding(4, 2)]] Texture2D<float4> _normalTexture;
[[vk::binding(5, 2)]] SamplerState _normalTextureSampler;
[[vk::binding(6, 2)]] Texture2D<float4> _metallicRoughnessTexture;
[[vk::binding(7, 2)]] SamplerState _metallicRoughnessTextureSampler;

public struct Surface : ISurface
{
    public float4 GetBaseColor(SurfaceInput input)
    {
        return _albedoTexture.Sample(_albedoTextureSampler, input.uv) * input.baseColorFactor;
    }

    public float3 GetNormalTS(SurfaceInput input)
    {
        float2 normalXY = _normalTexture.Sample(_normalTextureSampler, input.uv).rg * 2.0 - 1.0;
        return float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    }

    public float3 GetMetallicRoughnessAO(SurfaceInput input)
    {
        float4 mrTex = _metallicRoughnessTexture.Sample(_metallicRoughnessTextureSampler, input.uv);
        return float3(
            input.metallicRoughnessAO.x * mrTex.b,
            input.metallicRoughnessAO.y * mrTex.g,
            input.metallicRoughnessAO.z);
    }

    public float3 GetEmissive(SurfaceInput input)
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

    public void ModifyVertex(inout float3 worldPos, inout float3 normalWS, float2 uv)
    {
    }
}
