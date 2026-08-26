// file "particle2d-animated.slang" - module "particle2_d_animated"
//
// Slang module converted from Shaders/Pipelines/Rendering/Particle/Particle2DAnimated.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module particle2d_animated;

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

// same struct as Alco.Rendering.ParticleData2D in CSharp (ParticleData2D.cs)
public struct ParticleData2D {
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

public ParameterBlock<CameraParams> camera;


public struct MaterialParamsData
{
    public Texture2D texture;
};

public ParameterBlock<MaterialParamsData> material;

public struct ParticlesParams
{
    public RWStructuredBuffer<ParticleData2D> particles;
};

public ParameterBlock<ParticlesParams> particles;

public struct DataParams
{
    public uint framePerRow;
    public uint framePerColumn;
};

public ParameterBlock<DataParams> data;


[shader("vertex")] 
V2F MainVS(Vertex input) {
    V2F output = (V2F)0;
    ParticleData2D particle = particles.particles[input.instanceID];

    float3 position = input.position;
    position.xy *= particle.transform.scale;
    position.xy = rotate(position.xy, particle.transform.rotation);
    position.z += particle.depth;

    position.xy += particle.transform.position;

    //  apply camera transform
    output.position = mul(camera.viewProjection, float4(position, 1.0));

    uint framePerRow2 = max(data.framePerRow, 1);
    uint framePerColumn2 = max(data.framePerColumn, 1);

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
    output.instanceID = input.instanceID;
    output.color = particle.color;

    return output;
}

[shader("fragment")] 
float4 MainPS(V2F input): SV_TARGET {

  // Sample texture
  float4 texColor = material.texture.Sample(samplers.linearRepeat, input.uv);

  // Multiply texture color with particle color
  float4 finalColor = texColor * input.color;

  return finalColor;
}
