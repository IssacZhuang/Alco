#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/Transform2D.hlsli"

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

// same struct as Alco.Rendering.ParticleData2D in CSharp (ParticleData2D.cs)
struct ParticleData2D {
  Transform2D transform;
  float2 velocity;
  float4 color;
  float lifetime;
  float duration;
  float depth;
  float depthVelocity;
};

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

DEFINE_STORAGE(2, ParticleData2D, _particles);

DEFINE_UNIFORM(3, _data) { 
    uint framePerRow;
    uint framePerColumn;
};

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

    uint framePerRow2 = max(framePerRow, 1);
    uint framePerColumn2 = max(framePerColumn, 1);

    // Compute current frame index based on lifetime ratio
    float t = saturate(particle.lifetime / max(particle.duration, 1e-5));
    uint totalFrames = framePerRow2 * framePerColumn2;
    uint frame = (uint)(t * totalFrames);
    frame = min(frame, totalFrames - 1);

    // Derive cell position on the sprite sheet and compute UV remap
    uint frameColumn = frame % framePerRow2;
    uint frameRow = frame / framePerRow2;
    float2 cellSize = float2(1.0 / framePerRow2, 1.0 / framePerColumn2);
    float2 uvOffset = float2(frameColumn, frameRow) * cellSize;

    // Pass per-frame UV coordinates and instance ID
    output.uv = input.uv * cellSize + uvOffset;
    output.instanceId = input.instanceId;
    output.color = particle.color;

    return output;
}

[shader("pixel")] 
float4 MainPS(V2F input): SV_TARGET {

  // Sample texture
  float4 texColor = SAMPLE_TEX2D(_texture, input.uv);

  // Multiply texture color with particle color
  float4 finalColor = texColor * input.color;

  return finalColor;
}
