#language slang 2025
module AlcoGUI_Mask;

import Core;
import SharedSampler;

struct UIInstance
{
    float2 Origin;
    float2 Right;
    float2 Down;
    uint TextureIndex;
    uint Kind;
    float4 UvRect;
    float4 Color;
};

[GPURoot]
public struct UIMaskRoot
{
    public GPUBufferView Quads;
    public float2 ClipScale;
};

struct UIMaskVsOut
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
    nointerpolation uint TextureIndex : TEXCOORD1;
};

[shader("vertex")]
UIMaskVsOut vsMain(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    UIMaskRoot root = gpu_load_root<UIMaskRoot>();
    UIInstance inst = gpu_load<UIInstance>(root.Quads, instanceId);

    float2 local;
    switch (vertexId)
    {
    case 0u: local = float2(0.0, 0.0); break;
    case 1u: local = float2(1.0, 0.0); break;
    case 2u: local = float2(1.0, 1.0); break;
    case 3u: local = float2(0.0, 0.0); break;
    case 4u: local = float2(1.0, 1.0); break;
    default: local = float2(0.0, 1.0); break;
    }

    float2 worldPos = inst.Origin + inst.Right * local.x + inst.Down * local.y;

    UIMaskVsOut output;
    output.Position = float4(worldPos * root.ClipScale, 0.0, 1.0);
    output.Uv = inst.UvRect.xy + inst.UvRect.zw * local;
    output.TextureIndex = inst.TextureIndex;
    return output;
}

[shader("pixel")]
float4 psMain(UIMaskVsOut input) : SV_Target0
{
    if (input.TextureIndex != 0u)
    {
        Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(input.TextureIndex));
        float alpha = tex.SampleLevel(shared_sampler_linear_clamp(), input.Uv, 0.0).a;
        if (alpha < 0.5)
            discard;
    }
    return float4(0.0, 0.0, 0.0, 0.0);
}
