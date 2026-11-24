// file "TextureCompressBc5.slang" - module "TextureCompressBc5"

#language slang 2025
module TextureCompressBc5;

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
        gpu_store<uint4>(root.Output, index, CompressBc5(pixels));
    }
}
