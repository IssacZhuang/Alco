import lib;

//shaders.slang

//
// This file provides a simple vertex and fragment shader that can be compiled
// using Slang. This code should also be valid as HLSL, and thus it does not
// use any of the new language features supported by Slang.
//

#define DEFINE_BUFFER(set) [[vk_binding(0, set)]] cbuffer

#pragma BlendState AlphaBlend
#pragma DepthStencilState Read

// Uniform data to be passed from application -> shader.
[[vk_binding(0, 0)]]
cbuffer UniformsVertex
{
    float4x4 modelViewProjection;
}


DEFINE_BUFFER(1) UniformsFragment
{
    float4 inputColor;
}

// Per-vertex attributes to be assembled from bound vertex buffers.
struct VertexInput
{
    float3	position : POSITION;
    float3	color    : COLOR;
};


// Output of the vertex shader, and input to the fragment shader.
struct CoarseVertex
{
    float3 color;
};

// Output of the fragment shader
struct Fragment
{
    float4 color;
};

// Vertex  Shader

struct VertexOutput
{
    CoarseVertex    coarseVertex    : CoarseVertex;
    float4          sv_position     : SV_Position;
};


[shader("vertex")]
VertexOutput vertexMain(
    VertexInput vertexInput)
{
    VertexOutput output;

    float3 position = vertexInput.position;
    float3 color    = vertexInput.color;

    output.coarseVertex.color = color * inputColor.xyz;
    output.sv_position = mul(modelViewProjection, float4(position, 1.0));

    return output;
}

// Fragment Shader

[shader("fragment")]
float4 fragmentMain(
    CoarseVertex coarseVertex : CoarseVertex) : SV_Target
{
    float3 color = coarseVertex.color;
    float grayscale = grayscale(color);
    return float4(grayscale, grayscale, grayscale, 1.0) * inputColor;
}