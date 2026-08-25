// module "pbr_standard"
//
// The built-in glTF metallic-roughness surface: the four standard texture
// slots, each multiplied with its per-instance factor. Slang counterpart of
// Shaders/Materials/PbrStandard.hlsli - the surface is a module exporting
// `public struct Surface : ISurface`; its resources live at module scope in
// one ParameterBlock (the compiler assigns its descriptor set; members
// resolve by bare field name and the engine binds them by name). No pass
// knowledge lives in this file. Vertex deformation rides the IVertexSurface
// default (identity).

#language slang 2025
module pbr_standard;

import alco_rendering_core;
import alco_world3d_surface;

// Texture slots only — the glTF factors are per-instance data riding the
// renderers' instance buffers, so the block carries no [MaterialParams]
// uniform members (the marker's float-packing contract would reject it).
public struct MaterialParamsData
{
    public Texture2D<float4> _albedoTexture;
    public Texture2D<float4> _normalTexture;
    public Texture2D<float4> _metallicRoughnessTexture;
    public Texture2D<float4> _emissiveTexture;
};

public ParameterBlock<MaterialParamsData> _material;

public struct Surface : ISurface
{
    public override float4 GetBaseColor(SurfaceInput input)
    {
        return _material._albedoTexture.Sample(_samplers._linearRepeat, input.uv) * input.baseColorFactor;
    }

    public override float3 GetNormalTS(SurfaceInput input)
    {
        // Two-channel tangent-space normal map (BC5); z is reconstructed.
        float2 normalXY = _material._normalTexture.Sample(_samplers._linearRepeat, input.uv).rg * 2.0 - 1.0;
        return float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    }

    public override float3 GetMetallicRoughnessAO(SurfaceInput input)
    {
        // glTF metallic-roughness texture: roughness in G, metallic in B, both
        // multiplied with their factors. AO stays factor-only (no AO texture).
        float4 mrTex = _material._metallicRoughnessTexture.Sample(_samplers._linearRepeat, input.uv);
        return float3(
            input.metallicRoughnessAO.x * mrTex.b,
            input.metallicRoughnessAO.y * mrTex.g,
            input.metallicRoughnessAO.z);
    }

    public override float3 GetEmissive(SurfaceInput input)
    {
        // Emissive texture (sRGB-decoded by the sampler) times the linear factor.
        return _material._emissiveTexture.Sample(_samplers._linearRepeat, input.uv).rgb * input.emissiveFactor.rgb;
    }

    // Voxel-GI feed: the compute stage has no implicit derivatives, so the feed
    // samples with the explicit LOD the voxelizer passes (a coarse mip averages
    // sub-triangle detail).
    public override float4 GetVoxelBaseColor(SurfaceInput input, float lod)
    {
        return _material._albedoTexture.SampleLevel(_samplers._linearRepeat, input.uv, lod) * input.baseColorFactor;
    }

    public override float3 GetVoxelEmissive(SurfaceInput input, float lod)
    {
        return _material._emissiveTexture.SampleLevel(_samplers._linearRepeat, input.uv, lod).rgb * input.emissiveFactor.rgb;
    }
}
