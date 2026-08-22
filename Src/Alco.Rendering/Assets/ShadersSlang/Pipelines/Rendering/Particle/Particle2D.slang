// file "Particle2D.slang" - module "particle2_d"
//
// Slang module converted from Shaders/Pipelines/Rendering/Particle/Particle2D.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module particle2_d;

import alco_rendering_core;
import alco_rendering_transform2d;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

struct Vertex {
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    uint instanceId : SV_VulkanInstanceID;
};

struct V2F {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    uint instanceId : TEXCOORD1;
    float4 color : COLOR0;
};

//same struct as Alco.Rendering.ParticleData2D in CSharp (ParticleData2D.cs)
struct ParticleData2D{
    Transform2D transform;
    float2 velocity;
    float4 color;
    float lifetime;
    float duration;
    float depth;
    float depthVelocity;
};

[[vk::binding(0, 0)]] cbuffer _camera { float4x4 viewProjection; };


[[vk::binding(0, 1)]] Texture2D _texture;
[[vk::binding(1, 1)]] SamplerState _textureSampler;

[[vk::binding(0, 2)]] RWStructuredBuffer<ParticleData2D> _particles;

[shader("vertex")]
V2F MainVS(Vertex input) {
    V2F output = (V2F)0;
    ParticleData2D particle = _particles[input.instanceId];
    
    float3 position = input.position;
    position.xy *= particle.transform.scale;
    position.xy = rotate(position.xy, particle.transform.rotation);
#if defined(IS_FACADE)
    position.z = -position.y;
#endif
    position.z += particle.depth;

    position.xy += particle.transform.position;

    //  apply camera transform
    output.position = mul(viewProjection, float4(position, 1.0));
    
    // Pass UV coordinates and instance ID
    output.uv = input.uv;
    output.instanceId = input.instanceId;
    output.color = particle.color;
    
    return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
    
    // Sample texture
    float4 texColor = SAMPLE_TEX2D(_texture, input.uv);
    
    // Multiply texture color with particle color
    float4 finalColor = texColor * input.color;
    
    
    return finalColor;
}
