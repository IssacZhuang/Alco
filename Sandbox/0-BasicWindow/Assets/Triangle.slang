// file "Triangle.slang" - module "Triangle"

#language slang 2025
module Triangle;

import Core;

struct TriangleVertex
{
    float4 Position;
    float4 Color;
};

[GPURoot]
public struct TriangleRoot
{
    public GPUBufferView Vertices;
};

struct VertexOutput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
};

[shader("vertex")]
VertexOutput MainVS(uint vertexId : SV_VertexID)
{
    TriangleRoot root = gpu_load_root<TriangleRoot>();
    TriangleVertex vertex = gpu_load<TriangleVertex>(root.Vertices, vertexId);

    VertexOutput output;
    output.Position = vertex.Position;
    output.Color = vertex.Color;
    return output;
}

[shader("pixel")]
float4 MainPS(VertexOutput input) : SV_Target0
{
    return input.Color;
}
