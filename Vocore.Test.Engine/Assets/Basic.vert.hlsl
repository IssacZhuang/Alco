struct Vertex
{
    float3 pos : POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};

struct PixelInput
{
    float4 pos : SV_POSITION;
    float4 color : COLOR0;
    float2 uv : TEXCOORD0;
};

PixelInput main(Vertex input)
{
    PixelInput output;
    output.pos = float4(input.pos, 1.0f);
    output.uv = input.uv;
    output.color = input.color;
    return output;
}

