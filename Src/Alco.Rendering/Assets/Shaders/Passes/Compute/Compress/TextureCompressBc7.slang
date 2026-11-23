// file "TextureCompressBc7.slang" - module "TextureCompressBc7"

#language slang 2025
module TextureCompressBc7;

import Core;
import AlcoRendering_TextureCompression;
import AlcoRendering_TextureCompressionBc7;

[GPURoot]
public struct TextureCompressionRoot
{
    public GPUBufferView Output;
    public GpuTextureId Source;
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
        gpu_store<uint4>(root.Output, index, CompressBc7(pixels));
    }
}
