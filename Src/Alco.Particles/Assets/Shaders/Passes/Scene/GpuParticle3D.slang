// file "GpuParticle3D.slang" - module "GpuParticle3D"
//
// The built-in 3D particle render pass TEMPLATE: camera-facing billboard quads,
// drawn indexed-indirect through the emitter's render list (see GpuParticle2D
// for the pass family contract - the fragment entry is generic over the
// particle surface, composed with the group's material by the material
// compiler). The billboard basis (camera right/up in world space) arrives in
// the push constant; the particle's roll rotates the corner in screen space.
// Depth is tested but not written (the material's depth stencil state).

#language slang 2025
module GpuParticle3D;

import AlcoRendering_Core;
import AlcoParticles_Core2D; // particleRotate (screen-space roll)
import AlcoParticles_Core3D;
import AlcoParticles_Surface;

public struct Vertex
{
    public float3 position : POSITION;
    public float2 uv : TEXCOORD0;
    public uint instanceID : SV_VulkanInstanceID;
};

public struct V2F
{
    public float4 position : SV_POSITION;
    public float2 uv : TEXCOORD0;
    public float4 color : COLOR0;
    public float age01 : TEXCOORD1;
};

public struct CameraParams
{
    public float4x4 viewProjection;
};

public ParameterBlock<CameraParams> camera;

public struct SceneParams
{
    public RWStructuredBuffer<ParticleData3D> particles;
    public RWStructuredBuffer<EmitterParams3D> emitters;
    public RWStructuredBuffer<uint> renderList;
};

public ParameterBlock<SceneParams> scene;

public struct GpuParticleDraw3D
{
    public float4x4 model;        // emitter transform (local space) or identity (world space)
    public float4 cameraRight;    // world-space camera right (xyz)
    public float4 cameraUp;       // world-space camera up (xyz)
    public uint emitterSlot;
    public uint pad0;
    public uint pad1;
    public uint pad2;
};

[[vk::push_constant]] GpuParticleDraw3D pc;

[shader("vertex")]
public V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    EmitterParams3D emitter = scene.emitters[pc.emitterSlot];
    uint local = scene.renderList[emitter.sliceOffset + input.instanceID];
    ParticleData3D particle = scene.particles[emitter.sliceOffset + local];

    float2 corner = input.position.xy * particle.size;
    corner = particleRotate(corner, particle.rotation);
    float3 center = mul(pc.model, float4(particle.position, 1.0)).xyz;
    float3 world = center + pc.cameraRight.xyz * corner.x + pc.cameraUp.xyz * corner.y;
    output.position = mul(camera.viewProjection, float4(world, 1.0));

    float age = max(particle.duration - particle.lifetime, 0.0);
    float2 uv = input.uv;
    uint rows = uint(emitter.flipbook.x);
    uint cols = uint(emitter.flipbook.y);
    if (rows * cols > 1u)
    {
        uint frame = uint(age * emitter.flipbook.z);
        uint total = rows * cols;
        frame = emitter.flipbook.w > 0.5 ? frame % total : min(frame, total - 1u);
        float2 cell = float2(float(frame % cols), float(frame / cols));
        uv = (uv + cell) / float2(float(cols), float(rows));
    }
    output.uv = uv;
    output.color = particle.color * emitter.tint;
    output.age01 = particle.duration > 0.0 ? saturate(age / particle.duration) : 0.0;
    return output;
}

[shader("fragment")]
public float4 MainPS<T : IParticleSurface>(V2F input) : SV_TARGET
{
    T surface = T();
    ParticleSurfaceInput surfaceInput;
    surfaceInput.uv = input.uv;
    surfaceInput.color = input.color;
    surfaceInput.age01 = input.age01;
    return surface.shade(surfaceInput);
}
