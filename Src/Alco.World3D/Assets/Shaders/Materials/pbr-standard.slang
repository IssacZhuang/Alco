// module "pbr_standard"
//
// The built-in glTF metallic-roughness surface: the four standard texture
// slots, each multiplied with its per-instance factor. Slang counterpart of
// Shaders/Materials/PbrStandard.hlsli - the surface is a module exporting
// `public struct Surface : ISurface`; its resources live at module scope in a
// set-scoped block and the engine binds them by name. No pass knowledge lives
// in this file. Vertex deformation rides the IVertexSurface default (identity).

#language slang 2025
module pbr_standard;

import alco_world3d_surface;

cbuffer _material : register(b0, space2)
{
    Texture2D<float4> _albedoTexture;
    SamplerState _albedoTextureSampler;
    Texture2D<float4> _normalTexture;
    SamplerState _normalTextureSampler;
    Texture2D<float4> _metallicRoughnessTexture;
    SamplerState _metallicRoughnessTextureSampler;
    Texture2D<float4> _emissiveTexture;
    SamplerState _emissiveTextureSampler;
}

public struct Surface : ISurface
{
    public override float4 GetBaseColor(SurfaceInput input)
    {
        return _albedoTexture.Sample(_albedoTextureSampler, input.uv) * input.baseColorFactor;
    }

    public override float3 GetNormalTS(SurfaceInput input)
    {
        // Two-channel tangent-space normal map (BC5); z is reconstructed.
        float2 normalXY = _normalTexture.Sample(_normalTextureSampler, input.uv).rg * 2.0 - 1.0;
        return float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    }

    public override float3 GetMetallicRoughnessAO(SurfaceInput input)
    {
        // glTF metallic-roughness texture: roughness in G, metallic in B, both
        // multiplied with their factors. AO stays factor-only (no AO texture).
        float4 mrTex = _metallicRoughnessTexture.Sample(_metallicRoughnessTextureSampler, input.uv);
        return float3(
            input.metallicRoughnessAO.x * mrTex.b,
            input.metallicRoughnessAO.y * mrTex.g,
            input.metallicRoughnessAO.z);
    }

    public override float3 GetEmissive(SurfaceInput input)
    {
        // Emissive texture (sRGB-decoded by the sampler) times the linear factor.
        return _emissiveTexture.Sample(_emissiveTextureSampler, input.uv).rgb * input.emissiveFactor.rgb;
    }

    // Voxel-GI feed: the compute stage has no implicit derivatives, so the feed
    // samples with the explicit LOD the voxelizer passes (a coarse mip averages
    // sub-triangle detail).
    public override float4 GetVoxelBaseColor(SurfaceInput input, float lod)
    {
        return _albedoTexture.SampleLevel(_albedoTextureSampler, input.uv, lod) * input.baseColorFactor;
    }

    public override float3 GetVoxelEmissive(SurfaceInput input, float lod)
    {
        return _emissiveTexture.SampleLevel(_emissiveTextureSampler, input.uv, lod).rgb * input.emissiveFactor.rgb;
    }
}
