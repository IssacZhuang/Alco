// file "particle2d.slang" - module "particle2_d"
//
// Slang module converted from Shaders/Pipelines/Rendering/Particle/Particle2D.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module particle2d;

import alco_rendering_core;
import alco_rendering_transform2d;

struct Vertex {
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    uint instanceID : SV_VulkanInstanceID;
};

struct V2F {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    uint instanceID : TEXCOORD1;
    float4 color : COLOR0;
};

//same struct as Alco.Rendering.ParticleData2D in CSharp (ParticleData2D.cs)
public struct ParticleData2D{
    Transform2D transform;
    float2 velocity;
    float4 color;
    float lifetime;
    float duration;
    float depth;
    float depthVelocity;
};

public struct CameraParams
{
    public float4x4 viewProjection;
};

public ParameterBlock<CameraParams> _camera;


public struct MaterialParamsData
{
    public Texture2D _texture;
};

public ParameterBlock<MaterialParamsData> _material;

public struct ParticlesParams
{
    public RWStructuredBuffer<ParticleData2D> _particles;
};

public ParameterBlock<ParticlesParams> _particles;

[shader("vertex")]
V2F MainVS(Vertex input) {
    V2F output = (V2F)0;
    ParticleData2D particle = _particles._particles[input.instanceID];
    
    float3 position = input.position;
    position.xy *= particle.transform.scale;
    position.xy = rotate(position.xy, particle.transform.rotation);
    position.z += particle.depth;

    position.xy += particle.transform.position;

    //  apply camera transform
    output.position = mul(_camera.viewProjection, float4(position, 1.0));
    
    // Pass UV coordinates and instance ID
    output.uv = input.uv;
    output.instanceID = input.instanceID;
    output.color = particle.color;
    
    return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
    
    // Sample texture
    float4 texColor = _material._texture.Sample(_samplers._linearRepeat, input.uv);
    
    // Multiply texture color with particle color
    float4 finalColor = texColor * input.color;
    
    
    return finalColor;
}
