struct Vertex
{
    float3 pos : POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
}

struct PixelInput
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
}

PixelInput vertex(Vertex input)
{
    PixelInput output;
    //output.pos = mul(input.pos, WorldViewProjection);
    output.uv = input.uv;
    output.color = input.color;
    return output;
}

