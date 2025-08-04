#include "Shaders/Libs/Core.hlsli"

struct Vertex {
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    uint instanceId : SV_INSTANCEID;
};

struct V2F {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    uint instanceId : TEXCOORD1;
    float4 color : COLOR0;
};

//same struct as Alco.Rendering.ParticleData2D in CSharp (ParticleData2D.cs)
struct ParticleData2D{
    float2 position;
    float2 rotation; // x, y represent the sin and cos of the rotation
    float2 scale;
    float2 velocity;
    float4 color;
    float lifetime;
    float duration;
};

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

DEFINE_STORAGE(2, ParticleData2D, _particles);

[shader("vertex")]
V2F MainVS(Vertex input) {
    V2F output = (V2F)0;
    ParticleData2D particle = _particles[input.instanceId];
    
    float2 scaledPosition = input.position.xy * particle.scale;

    // rotation
    float2x2 rotMatrix = {
        particle.rotation.y, -particle.rotation.x,
        particle.rotation.x, particle.rotation.y
    };
    float2 rotatedPosition = mul(rotMatrix, scaledPosition);

    // transform mesh by position and apply camera transform
    float3 worldPosition = float3(rotatedPosition + particle.position, 0.0);
    output.position = mul(viewProjection, float4(worldPosition, 1.0));
    
    // Pass UV coordinates and instance ID
    output.uv = input.uv;
    output.instanceId = input.instanceId;
    output.color = particle.color;
    
    return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
    
    // Sample texture
    float4 texColor = SAMPLE_TEX2D(_texture, input.uv);
    
    // Multiply texture color with particle color
    float4 finalColor = texColor * input.color;
    
    
    return finalColor;
}
