// module "pbr_standard"
//
// The built-in glTF metallic-roughness surface: the four standard texture
// slots, each multiplied with its per-instance factor. Slang counterpart of
// Shaders/Materials/PbrStandard.hlsli - the surface is a module exporting
// `public struct Surface : ISurface`; its resources live at module scope and
// the engine binds them by name (see StandardSurfaceSlotsUtility). No pass
// knowledge lives in this file.

import surface;

Texture2D<float4> _albedoTexture;
SamplerState _albedoTextureSampler;
Texture2D<float4> _normalTexture;
SamplerState _normalTextureSampler;
Texture2D<float4> _metallicRoughnessTexture;
SamplerState _metallicRoughnessTextureSampler;
Texture2D<float4> _emissiveTexture;
SamplerState _emissiveTextureSampler;

public struct Surface : ISurface
{
    public float4 GetBaseColor(SurfaceInput input)
    {
        return _albedoTexture.Sample(_albedoTextureSampler, input.uv) * input.baseColorFactor;
    }

    public float3 GetNormalTS(SurfaceInput input)
    {
        // Two-channel tangent-space normal map (BC5); z is reconstructed.
        float2 normalXY = _normalTexture.Sample(_normalTextureSampler, input.uv).rg * 2.0 - 1.0;
        return float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    }

    public float3 GetMetallicRoughnessAO(SurfaceInput input)
    {
        // glTF metallic-roughness texture: roughness in G, metallic in B, both
        // multiplied with their factors. AO stays factor-only (no AO texture).
        float4 mrTex = _metallicRoughnessTexture.Sample(_metallicRoughnessTextureSampler, input.uv);
        return float3(
            input.metallicRoughnessAO.x * mrTex.b,
            input.metallicRoughnessAO.y * mrTex.g,
            input.metallicRoughnessAO.z);
    }

    public float3 GetEmissive(SurfaceInput input)
    {
        // Emissive texture (sRGB-decoded by the sampler) times the linear factor.
        return _emissiveTexture.Sample(_emissiveTextureSampler, input.uv).rgb * input.emissiveFactor.rgb;
    }

    // Identity vertex deformation: the standard surface does not animate vertices.
    public void ModifyVertex(inout float3 worldPos, inout float3 normalWS, float2 uv)
    {
    }
}
