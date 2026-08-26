// file "shader.slang" - module "sandbox1_shader"

#language slang 2025
module sandbox1_shader;

struct Vertex{
    float3 position : POSITION;
    float4 color : COLOR;
};

struct PixelInput{
    float4 position : SV_POSITION;
    float4 color : COLOR;
};

[shader("vertex")]
PixelInput MainVS(Vertex input) {
    PixelInput output;
    output.position = float4(input.position, 1.0f);
    output.color = input.color;
    return output;
}

[shader("fragment")]
float4 MainPS(PixelInput input) : SV_TARGET {
    return input.color;
}
