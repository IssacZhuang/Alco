// module "pbr_standard"
//
// The built-in glTF metallic-roughness surface: the four standard texture
// slots, each multiplied with its per-instance factor. The surface is a module
// exporting its
// single ISurface implementation; its resources live at module scope in
// one ParameterBlock (the compiler assigns its descriptor set; members
// resolve by bare field name and the engine binds them by name). No pass
// knowledge lives in this file. Vertex deformation rides the IVertexSurface
// default (identity).

#language slang 2025
module PbrStandard;

import AlcoRendering_Core;
import AlcoWorld3D_Surface;

// Texture slots only — the glTF factors are per-instance data riding the
// renderers' instance buffers, so the block carries no [MaterialParams]
// uniform members (the marker's float-packing contract would reject it).
public struct MaterialParamsData
{
    public Texture2D<float4> albedoTexture;
    public Texture2D<float4> normalTexture;
    public Texture2D<float4> metallicRoughnessTexture;
    public Texture2D<float4> emissiveTexture;
};

public ParameterBlock<MaterialParamsData> material;

public struct Surface : ISurface
{
    public override float4 getBaseColor(SurfaceInput input)
    {
        return material.albedoTexture.Sample(samplers.linearRepeat, input.uv) * input.baseColorFactor;
    }

    public override float3 getNormalTS(SurfaceInput input)
    {
        // Two-channel tangent-space normal map (BC5); z is reconstructed.
        float2 normalXY = material.normalTexture.Sample(samplers.linearRepeat, input.uv).rg * 2.0 - 1.0;
        return float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    }

    public override float3 getMetallicRoughnessAO(SurfaceInput input)
    {
        // glTF metallic-roughness texture: roughness in G, metallic in B, both
        // multiplied with their factors. AO stays factor-only (no AO texture).
        float4 mrTex = material.metallicRoughnessTexture.Sample(samplers.linearRepeat, input.uv);
        return float3(
            input.metallicRoughnessAO.x * mrTex.b,
            input.metallicRoughnessAO.y * mrTex.g,
            input.metallicRoughnessAO.z);
    }

    public override float3 getEmissive(SurfaceInput input)
    {
        // Emissive texture (sRGB-decoded by the sampler) times the linear factor.
        return material.emissiveTexture.Sample(samplers.linearRepeat, input.uv).rgb * input.emissiveFactor.rgb;
    }

    // Voxel-GI feed: the compute stage has no implicit derivatives, so the feed
    // samples with the explicit LOD the voxelizer passes (a coarse mip averages
    // sub-triangle detail).
    public override float4 getVoxelBaseColor(SurfaceInput input, float lod)
    {
        return material.albedoTexture.SampleLevel(samplers.linearRepeat, input.uv, lod) * input.baseColorFactor;
    }

    public override float3 getVoxelEmissive(SurfaceInput input, float lod)
    {
        return material.emissiveTexture.SampleLevel(samplers.linearRepeat, input.uv, lod).rgb * input.emissiveFactor.rgb;
    }
}
