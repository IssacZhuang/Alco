// file "GpuParticle3D.slang" - module "GpuParticle3D"
//
// The built-in 3D particle render pass TEMPLATE: camera-facing billboard quads,
// drawn indexed-indirect through the emitter's render list (see GpuParticle2D
// for the pass family contract - both entries are generic over the particle
// surface (IParticleSurface, AlcoParticles_Surface — the dimension-neutral
// contract shared with the 2D pass), composed with the group's material by the
// material compiler). The billboard basis (camera right/up in world space)
// arrives in the push constant; the particle's roll rotates the corner in
// screen space.
// Groups flagged velocity-stretched instead stretch the billboard along the
// screen-space projection of the particle's velocity. The "overLife" block
// carries the group's baked color-gradient/size-curve lookup textures.
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

public struct OverLifeResources
{
    // The group's baked over-life lookup textures (256x1): the color gradient
    // (RGBA8) and the size curve (R16Float - curve values may exceed 1). Both
    // bind a 1x1 identity texture when the group authors none; the
    // PARTICLE_FLAG_COLOR_GRADIENT/PARTICLE_FLAG_SIZE_CURVE bits gate the fetch.
    public Texture2D colorGradient;
    public Texture2D<float> sizeCurve;
};

public ParameterBlock<OverLifeResources> overLife;

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
public V2F MainVS<T : IParticleSurface>(Vertex input)
{
    V2F output = (V2F)0;
    EmitterParams3D emitter = scene.emitters[pc.emitterSlot];
    uint local = scene.renderList[emitter.sliceOffset + input.instanceID];
    ParticleData3D particle = scene.particles[emitter.sliceOffset + local];

    float age = max(particle.duration - particle.lifetime, 0.0);
    float age01 = particle.duration > 0.0 ? saturate(age / particle.duration) : 0.0;

    // Size over life: the baked size curve (PARTICLE_FLAG_SIZE_CURVE) replaces
    // the simulate pass's end-scale lerp; it multiplies the spawn size.
    float size = particle.size;
    if ((emitter.flags & PARTICLE_FLAG_SIZE_CURVE) != 0u)
    {
        size = particle.startSize * overLife.sizeCurve.Sample(samplers.linearClamp, float2(age01, 0.5));
    }

    float3 center = mul(pc.model, float4(particle.position, 1.0)).xyz;
    float3 world;
    if ((emitter.flags & PARTICLE_FLAG_VELOCITY_STRETCH) != 0u)
    {
        // Velocity-stretched billboard: the stretch axis is the particle's
        // velocity projected onto the camera plane (in the right/up basis), the
        // length grows with the speed; the perpendicular keeps the base size.
        // Speed ~ 0 (or a velocity pointing at the camera) keeps the plain
        // camera-facing quad with its roll.
        float3 worldVelocity = mul(pc.model, float4(particle.velocity, 0.0)).xyz;
        float2 planeVelocity = float2(
            dot(worldVelocity, pc.cameraRight.xyz),
            dot(worldVelocity, pc.cameraUp.xyz));
        float planeSpeed = length(planeVelocity);
        if (planeSpeed > 1e-4)
        {
            float2 axis2 = planeVelocity / planeSpeed;
            float3 axis = pc.cameraRight.xyz * axis2.x + pc.cameraUp.xyz * axis2.y;
            float3 perp = pc.cameraRight.xyz * (-axis2.y) + pc.cameraUp.xyz * axis2.x;
            float along = size * emitter.size.w + length(worldVelocity) * emitter.speed.w;
            world = center + axis * (input.position.x * along) + perp * (input.position.y * size);
        }
        else
        {
            float2 corner = particleRotate(input.position.xy * size, particle.rotation);
            world = center + pc.cameraRight.xyz * corner.x + pc.cameraUp.xyz * corner.y;
        }
    }
    else
    {
        float2 corner = particleRotate(input.position.xy * size, particle.rotation);
        world = center + pc.cameraRight.xyz * corner.x + pc.cameraUp.xyz * corner.y;
    }

    // The material's vertex-stage hook, fed through the dimension-neutral
    // ParticleVertexInput (same contract as the 2D pass): the default surface
    // returns the position unchanged.
    T surface = T();
    ParticleVertexInput vertexInput;
    vertexInput.world = world;
    vertexInput.particlePosition = center;
    vertexInput.age01 = age01;
    vertexInput.emitterWorldMatrix = emitter.worldMatrix;
    vertexInput.customData = asfloat(emitter.renderMisc.y);
    world = surface.adjustWorldPosition(vertexInput);
    output.position = mul(camera.viewProjection, float4(world, 1.0));

    float2 uv = input.uv;
    uint rows = uint(emitter.flipbook.x);
    uint cols = uint(emitter.flipbook.y);
    if (rows * cols > 1u)
    {
        // The frame follows the particle's age (or the remaining life when
        // PARTICLE_FLAG_FLIPBOOK_REVERSE is set — see the 2D pass).
        uint frame = uint(age * emitter.flipbook.z);
        uint total = rows * cols;
        frame = emitter.flipbook.w > 0.5 ? frame % total : min(frame, total - 1u);
        if ((emitter.flags & PARTICLE_FLAG_FLIPBOOK_REVERSE) != 0u)
        {
            frame = total - 1u - frame;
        }
        float2 cell = float2(float(frame % cols), float(frame / cols));
        uv = (uv + cell) / float2(float(cols), float(rows));
    }
    output.uv = uv;

    // Color over life: the baked gradient (PARTICLE_FLAG_COLOR_GRADIENT)
    // replaces the simulate
    // pass's spawn->end lerp (spawn color x gradient sample); the fade ramps
    // re-apply on top, unchanged.
    float4 color = particle.color;
    if ((emitter.flags & PARTICLE_FLAG_COLOR_GRADIENT) != 0u)
    {
        color = particle.startColor * overLife.colorGradient.Sample(samplers.linearClamp, float2(age01, 0.5));
        float fadeIn = emitter.life.z > 1e-5 ? smoothstep(0.0, emitter.life.z, age01) : 1.0;
        float fadeOut = emitter.life.w > 1e-5 ? 1.0 - smoothstep(1.0 - emitter.life.w, 1.0, age01) : 1.0;
        color.a *= fadeIn * fadeOut;
    }
    output.color = color * emitter.tint;
    output.age01 = age01;
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
