#language slang 2025
module ParameterizedSurface;

import Core;
import World3DSurface;

static const uint InvalidTexture = 0xffffffffu;

float4 sampleTexture(GPUTextureId id, SurfaceInput input)
{
    Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(id.index));
    SamplerState sampler = gpu_sampler(gpu_nonuniform_index(input.Material.Sampler.index));
    return tex.SampleGrad(sampler, input.UV, input.DdxUV, input.DdyUV);
}

float4 sampleTextureLevel(GPUTextureId id, GPUSamplerId samplerId, float2 uv, float lod)
{
    Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(id.index));
    SamplerState sampler = gpu_sampler(gpu_nonuniform_index(samplerId.index));
    return tex.SampleLevel(sampler, uv, lod);
}

public struct ParameterizedSurface : ISurface
{
    public override float4 getVoxelBaseColor(VoxelSurfaceInput input)
    {
        float4 factor = input.Material.BaseColor;
        GPUTextureId texture = input.Material.BaseColorTexture;
        if (texture.index != InvalidTexture)
            factor *= sampleTextureLevel(texture, input.Material.Sampler, input.UV, input.TextureLod);
        return float4(factor.rgb * float3(0.4, 1.0, 0.75), factor.a);
    }

    public override float3 getVoxelEmissive(VoxelSurfaceInput input)
    {
        float3 factor = input.Material.EmissiveAlphaCutoff.rgb;
        GPUTextureId texture = input.Material.EmissiveTexture;
        if (texture.index != InvalidTexture)
            factor *= sampleTextureLevel(texture, input.Material.Sampler, input.UV, input.TextureLod).rgb;
        return factor + float3(0.05, 0.25, 0.15);
    }

    public override float4 getBaseColor(SurfaceInput input)
    {
        float4 baseCol = input.Material.BaseColor;
        GPUTextureId texture = input.Material.BaseColorTexture;
        if (texture.index != InvalidTexture)
            baseCol *= sampleTexture(texture, input);

        // Distinct UV-modulated stripe pattern to identify custom ISurface shading in readback tests.
        float2 cell = floor(input.UV * 4.0);
        float checker = ((uint(cell.x) + uint(cell.y)) & 1u) != 0u ? 1.0 : 0.35;
        return float4(baseCol.rgb * float3(0.25, 1.0, 0.75) * checker, baseCol.a);
    }

    public override float3 getNormalTS(SurfaceInput input)
    {
        GPUTextureId texture = input.Material.NormalTexture;
        if (texture.index == InvalidTexture)
            return float3(0.0, 0.0, 1.0);

        float2 xy = sampleTexture(texture, input).rg * 2.0 - 1.0;
        return float3(xy, sqrt(saturate(1.0 - dot(xy, xy))));
    }

    public override float3 getMetallicRoughnessAO(SurfaceInput input)
    {
        float2 factors = input.Material.MetallicRoughness;
        GPUTextureId texture = input.Material.MetallicRoughnessTexture;
        if (texture.index != InvalidTexture)
            factors *= sampleTexture(texture, input).bg;
        return float3(factors, 1.0);
    }

    public override float3 getEmissive(SurfaceInput input)
    {
        float3 factor = input.Material.EmissiveAlphaCutoff.rgb;
        GPUTextureId texture = input.Material.EmissiveTexture;
        if (texture.index != InvalidTexture)
            factor *= sampleTexture(texture, input).rgb;
        return factor;
    }
};
