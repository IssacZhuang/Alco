// file "AlcoRendering_TextureCompression.slang" - module "AlcoRendering_TextureCompression"
//
// Shared bindless block loader and linear-to-sRGB conversion for BC1-BC7 compute encoders.

#language slang 2025
module AlcoRendering_TextureCompression;

import Core;

[ForceInline]
public float bcLinearToSrgbChannel(float linearValue)
{
    float c = saturate(linearValue);
    return c <= 0.0031308f ? c * 12.92f : 1.055f * pow(c, 1.0f / 2.4f) - 0.055f;
}

[ForceInline]
public float3 bcLinearToSrgb(float3 color)
{
    return float3(
        bcLinearToSrgbChannel(color.r),
        bcLinearToSrgbChannel(color.g),
        bcLinearToSrgbChannel(color.b));
}

public bool bcLoadCompressionBlock<let ConvertLinearToSrgb : int>(
    uint2 block,
    uint sourceIndex,
    uint mip,
    uint2 extent,
    out float4 pixels[16],
    out uint index)
{
    uint2 blocks = (extent + 3u) / 4u;
    index = block.y * blocks.x + block.x;
    if (any(block >= blocks))
    {
        for (uint k = 0u; k < 16u; ++k)
        {
            pixels[k] = float4(0.0f);
        }
        return false;
    }

    Texture2D<float4> source = gpu_texture<Texture2D<float4>>(sourceIndex);
    for (uint pixel = 0u; pixel < 16u; ++pixel)
    {
        uint2 position = min(block * 4u + uint2(pixel % 4u, pixel / 4u), extent - 1u);
        float4 texel = source.Load(int3(position, mip));
        if (ConvertLinearToSrgb != 0)
        {
            texel.rgb = bcLinearToSrgb(texel.rgb);
        }
        pixels[pixel] = texel;
    }
    return true;
}
