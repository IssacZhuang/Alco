#language slang 2025
module World3DPbrStandard;

import Core;
import World3DSurface;

// Texture.NoIndex in Alco.Graphics; descriptor zero is valid.
static const uint InvalidTexture = 0xffffffffu;

float4 sampleTexture(GPUTextureId id, SurfaceInput input)
{
    Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(id.index));
    SamplerState sampler = gpu_sampler(gpu_nonuniform_index(input.Material.Sampler.index));
    // The gradients are computed before any texture-presence or alpha-test divergence.
    return tex.SampleGrad(sampler, input.UV, input.DdxUV, input.DdyUV);
}

float4 sampleTextureLevel(GPUTextureId id, GPUSamplerId samplerId, float2 uv, float lod)
{
    Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(id.index));
    SamplerState sampler = gpu_sampler(gpu_nonuniform_index(samplerId.index));
    return tex.SampleLevel(sampler, uv, lod);
}

public struct PbrStandard : ISurface
{
    public override float4 getVoxelBaseColor(VoxelSurfaceInput input)
    {
        float4 factor = input.Material.BaseColor;
        GPUTextureId texture = input.Material.BaseColorTexture;
        if (texture.index == InvalidTexture)
            return factor;
        return factor * sampleTextureLevel(texture, input.Material.Sampler, input.UV, input.TextureLod);
    }

    public override float3 getVoxelEmissive(VoxelSurfaceInput input)
    {
        float3 factor = input.Material.EmissiveAlphaCutoff.rgb;
        GPUTextureId texture = input.Material.EmissiveTexture;
        if (texture.index == InvalidTexture)
            return factor;
        return factor * sampleTextureLevel(texture, input.Material.Sampler, input.UV, input.TextureLod).rgb;
    }
    public override float4 getBaseColor(SurfaceInput input)
    {
        // Base color and emissive use sRGB sampled views; RGB is decoded by the GPU, alpha is linear.
        float4 factor = input.Material.BaseColor;
        GPUTextureId texture = input.Material.BaseColorTexture;
        if (texture.index == InvalidTexture)
            return factor;
        return factor * sampleTexture(texture, input);
    }

    public override float3 getNormalTS(SurfaceInput input)
    {
        GPUTextureId texture = input.Material.NormalTexture;
        if (texture.index == InvalidTexture)
            return float3(0.0, 0.0, 1.0);

        // BC5/linear RG stores tangent-space XY, with Z on the positive hemisphere.
        float2 xy = sampleTexture(texture, input).rg * 2.0 - 1.0;
        return float3(xy, sqrt(saturate(1.0 - dot(xy, xy))));
    }

    public override float3 getMetallicRoughnessAO(SurfaceInput input)
    {
        float2 factors = input.Material.MetallicRoughness;
        GPUTextureId texture = input.Material.MetallicRoughnessTexture;
        if (texture.index != InvalidTexture)
            factors *= sampleTexture(texture, input).bg; // glTF B = metallic, G = roughness.
        return float3(factors, 1.0);
    }

    public override float3 getEmissive(SurfaceInput input)
    {
        float3 factor = input.Material.EmissiveAlphaCutoff.rgb;
        GPUTextureId texture = input.Material.EmissiveTexture;
        if (texture.index == InvalidTexture)
            return factor;
        return factor * sampleTexture(texture, input).rgb;
    }
};
