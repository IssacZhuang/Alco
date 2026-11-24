// file "TextureCompressBc4.slang" - module "TextureCompressBc4"

#language slang 2025
module TextureCompressBc4;

import Core;
import AlcoRendering_TextureCompression;
import AlcoRendering_TextureCompressionLegacy;

[GPURoot]
public struct TextureCompressionRoot
{
    public GPUBufferView Output;
    public GPUTextureId Source;
    public uint Mip;
    public uint2 Extent;
};

[shader("compute")]
[numthreads(8, 8, 1)]
void mainCS<let ConvertLinearToSrgb : int>(uint3 thread : SV_DispatchThreadID)
{
    TextureCompressionRoot root = gpu_load_root<TextureCompressionRoot>();
    float4 pixels[16];
    uint index;
    if (bcLoadCompressionBlock<ConvertLinearToSrgb>(thread.xy, root.Source.index, root.Mip, root.Extent, pixels, index))
    {
        gpu_store<uint2>(root.Output, index, CompressBc4(pixels, false));
    }
}
