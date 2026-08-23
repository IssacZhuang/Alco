// file "shader.slang" - module "sandbox2_shader"
//
// Slang module converted from Shader.hlsl (Phase 4 migration): the low-level
// sandbox compiles it through the native slang module API.

#language slang 2025
module sandbox2_shader;

struct Vertex{
    float3 position : POSITION;
    float4 color : COLOR;
};

struct PixelInput{
    float4 position : SV_POSITION;
};

cbuffer color : register(b0, space0) {
    float4 color;
}

[shader("vertex")]
PixelInput MainVS(Vertex input) {
    PixelInput output;
    output.position = float4(input.position, 1.0f);
    return output;
}

[shader("fragment")]
float4 MainPS(PixelInput input) : SV_TARGET {
    return color;
}
