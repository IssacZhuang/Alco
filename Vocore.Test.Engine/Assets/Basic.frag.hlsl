struct PixelInput
{
    float4 pos : SV_POSITION;
    float4 color : COLOR0;
    float2 uv : TEXCOORD0;
};


float4 main(PixelInput input) : SV_TARGET
{
    float4 color = input.color;
    return color;
}
