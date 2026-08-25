// file "shader.slang" - module "sandbox7_shader"
//
// Slang module converted from Shader.hlsl (Phase 4 migration): the low-level
// sandbox compiles it through the native slang module API. Resources are
// grouped as ParameterBlocks — the compiler assigns each block its own
// descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module sandbox7_shader;

#define MAX_INSTANCE_COUNT 500

struct Vertex2D{
    float2 position : POSITION;
    float2 uv : TEXCOORD0;
    uint instanceID : SV_VulkanInstanceID;
};

struct V2F{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

struct Constants{
    float4x4 model;
};

public struct ViewProjectionParams
{
    public float4x4 viewProjection;
};

public ParameterBlock<ViewProjectionParams> _viewProjection;

public struct MaterialParams
{
    public Texture2D image;
    public SamplerState imageSampler;
};

public ParameterBlock<MaterialParams> _material;

public struct PositionsParams
{
    public float4 Positions[MAX_INSTANCE_COUNT];
};

public ParameterBlock<PositionsParams> _positionsBuffer;

[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS(Vertex2D input) {
    float4 position = float4(input.position+_positionsBuffer.Positions[input.instanceID].xy, 0.0f, 1.0f);
    position = mul(constants.model, position);
    position = mul(_viewProjection.viewProjection, position);

    V2F output = (V2F)0;
    output.position = position;
    output.uv = input.uv;
    return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
    return _material.image.Sample(_material.imageSampler, input.uv);
}
