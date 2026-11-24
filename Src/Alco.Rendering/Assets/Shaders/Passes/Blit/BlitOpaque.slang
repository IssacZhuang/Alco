// file "BlitOpaque.slang" - module "BlitOpaque"

#language slang 2025
module BlitOpaque;

import Core;
import SharedSampler;

[GPURoot]
public struct BlitOpaqueRoot
{
    public GPUTextureId Texture;
};

struct V2F
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(uint vertexIndex : SV_VertexID)
{
    // Fullscreen triangle covering [-1, 1] x [-1, 1] with top-left UV (0, 0).
    float2 uv = float2((vertexIndex << 1) & 2, vertexIndex & 2);
    V2F output;
    output.Position = float4(uv * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    output.Uv = uv;
    return output;
}

// Forces alpha to 1 on the GPU so opaque captures from swapchains or unauthored
// render targets never require a CPU-side alpha pass before PNG encoding.
[shader("pixel")]
float4 MainPS(V2F input) : SV_Target0
{
    BlitOpaqueRoot root = gpu_load_root<BlitOpaqueRoot>();
    Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Texture.index);
    return float4(source.SampleLevel(shared_sampler_linear_clamp(), input.Uv, 0.0f).rgb, 1.0f);
}
