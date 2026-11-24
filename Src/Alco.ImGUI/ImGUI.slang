// file "ImGUI.slang" - module "ImGUI"
//
// Compiled through Alco.Graphics ShaderSystem from the embedded resource.
// Pulls packed 20-byte ImDrawVert records via GPUBufferView and samples bindless textures.

#language slang 2025
module ImGUI;

import Core;
import SharedSampler;

struct ImGuiVertex
{
    float2 Position;
    float2 Uv;
    uint Color;
};

[GPURoot]
public struct ImGuiRoot
{
    public float2 Scale;
    public float2 Translate;
    public GPUBufferView Vertices;
    public GPUTextureId Texture;
    public uint Padding0;
};

struct V2F
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
    float4 Color : COLOR0;
};

[shader("vertex")]
V2F MainVS(uint vertexId : SV_VertexID)
{
    ImGuiRoot root = gpu_load_root<ImGuiRoot>();
    ImGuiVertex vertex = gpu_load<ImGuiVertex>(root.Vertices, vertexId);

    V2F output;
    output.Position = float4(vertex.Position * root.Scale + root.Translate, 0.0f, 1.0f);
    output.Uv = vertex.Uv;
    output.Color = float4(
        vertex.Color & 255u,
        (vertex.Color >> 8) & 255u,
        (vertex.Color >> 16) & 255u,
        vertex.Color >> 24) / 255.0f;
    return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_Target0
{
    ImGuiRoot root = gpu_load_root<ImGuiRoot>();
    Texture2D<float4> texture = gpu_texture<Texture2D<float4>>(root.Texture.index);
    return input.Color * texture.Sample(shared_sampler_linear_clamp(), input.Uv);
}
