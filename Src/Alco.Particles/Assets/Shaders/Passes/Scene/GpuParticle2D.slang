// file "GpuParticle2D.slang" - module "GpuParticle2D"
//
// The built-in 2D particle render pass TEMPLATE: draws the emitter's quad mesh
// once per simulated particle (indexed-indirect, instance count written by the
// simulate pass), expanding the quad in the vertex shader from the particle
// record fetched through the emitter's render list. The fragment entry is
// generic over the particle surface (AlcoParticles_Surface): the material
// compiler composes this module with the group's material surface module, so
// the visual comes from the .amat - the built-in default surface
// (AlcoParticles_SurfaceDefault) shades texture x particle color.
//
// The pass owns the resource contract the particle system binds: the "camera"
// block (auto-bound view-projection), the "scene" block (bound by the particle
// system) and the GpuParticleDraw2D push constant. The surface module owns the
// material resources.

#language slang 2025
module GpuParticle2D;

import AlcoRendering_Core;
import AlcoParticles_Core2D;
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
    public RWStructuredBuffer<ParticleData2D> particles;
    public RWStructuredBuffer<EmitterParams2D> emitters;
    public RWStructuredBuffer<uint> renderList;
};

public ParameterBlock<SceneParams> scene;

public struct GpuParticleDraw2D
{
    public float4x4 model;       // emitter transform (local space) or identity (world space)
    public uint emitterSlot;
    public uint pad0;
    public uint pad1;
    public uint pad2;
};

[[vk::push_constant]] GpuParticleDraw2D pc;

[shader("vertex")]
public V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    EmitterParams2D emitter = scene.emitters[pc.emitterSlot];
    uint local = scene.renderList[emitter.sliceOffset + input.instanceID];
    ParticleData2D particle = scene.particles[emitter.sliceOffset + local];

    float2 corner = input.position.xy * particle.scale;
    corner = particleRotate(corner, particle.rotation);
    float3 world = mul(pc.model, float4(particle.position + corner, 0.0, 1.0)).xyz;
    output.position = mul(camera.viewProjection, float4(world, 1.0));

    // Flipbook: the frame follows the particle's age.
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
