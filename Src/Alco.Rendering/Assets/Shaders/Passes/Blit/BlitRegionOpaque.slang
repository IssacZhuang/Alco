// file "BlitRegionOpaque.slang" - module "BlitRegionOpaque"

#language slang 2025
module BlitRegionOpaque;

import Core;
import SharedSampler;

// Center-crop blit (see BlitRegion) that forces alpha to 1: capture blits route
// unauthored source alpha into read-back RGBA8, so opaque captures never need a
// CPU-side alpha pass.
[GPURoot]
public struct BlitRegionOpaqueRoot
{
    public GPUTextureId Texture;
    public uint Pad0;
    public float2 UvOffset; // Top-left (min) corner of the sampled source window, in [0,1].
    public float2 UvScale;  // Size of the sampled source window, in [0,1] on each axis.
    public float2 Pad1;
};

struct V2F
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(uint vertexIndex : SV_VertexID)
{
    BlitRegionOpaqueRoot root = gpu_load_root<BlitRegionOpaqueRoot>();
    // Fullscreen triangle covering [-1, 1] x [-1, 1] with top-left UV (0, 0).
    float2 uv = float2((vertexIndex << 1) & 2, vertexIndex & 2);
    V2F output;
    output.Position = float4(uv * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    // Map the [0,1] screen UV into the cropped source window.
    output.Uv = uv * root.UvScale + root.UvOffset;
    return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_Target0
{
    BlitRegionOpaqueRoot root = gpu_load_root<BlitRegionOpaqueRoot>();
    Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Texture.index);
    return float4(source.SampleLevel(shared_sampler_linear_clamp(), input.Uv, 0.0f).rgb, 1.0f);
}
