// file "shader.slang" - module "sandbox8_shader"
//
// Slang module converted from Shader.hlsl (Phase 4 migration, copied from 7-Instancing): the low-level
// sandbox compiles it through the native slang module API. Resources are
// grouped as ParameterBlocks — the compiler assigns each block its own
// descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module sandbox8_shader;

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

public ParameterBlock<ViewProjectionParams> viewProjection;

public struct MaterialParams
{
    public Texture2D image;
    public SamplerState imageSampler;
};

public ParameterBlock<MaterialParams> material;

public struct PositionsParams
{
    public float4 positions[MAX_INSTANCE_COUNT];
};

public ParameterBlock<PositionsParams> positionsBuffer;

[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS(Vertex2D input) {
    float4 position = float4(input.position+positionsBuffer.positions[input.instanceID].xy, 0.0f, 1.0f);
    position = mul(constants.model, position);
    position = mul(viewProjection.viewProjection, position);

    V2F output = (V2F)0;
    output.position = position;
    output.uv = input.uv;
    return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
    return material.image.Sample(material.imageSampler, input.uv);
}
