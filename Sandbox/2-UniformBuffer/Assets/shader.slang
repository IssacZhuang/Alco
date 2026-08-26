// file "shader.slang" - module "sandbox2_shader"

#language slang 2025
module sandbox2_shader;

struct Vertex{
    float3 position : POSITION;
    float4 color : COLOR;
};

struct PixelInput{
    float4 position : SV_POSITION;
};

public struct ColorParams
{
    public float4 color;
};

public ParameterBlock<ColorParams> color;

[shader("vertex")]
PixelInput MainVS(Vertex input) {
    PixelInput output;
    output.position = float4(input.position, 1.0f);
    return output;
}

[shader("fragment")]
float4 MainPS(PixelInput input) : SV_TARGET {
    return color.color;
}
