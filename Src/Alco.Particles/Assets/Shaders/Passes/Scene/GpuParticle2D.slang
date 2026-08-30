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
// system), the "overLife" block (the group's baked color-gradient/size-curve
// lookup textures) and the GpuParticleDraw2D push constant. The surface module
// owns the material resources.

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

public struct OverLifeResources
{
    // The group's baked over-life lookup textures (256x1): the color gradient
    // (RGBA8) and the size curve (R16Float - curve values may exceed 1). Both
    // bind a 1x1 identity texture when the group authors none; the
    // EmitterParams2D flag bits (1/2) gate the fetch.
    public Texture2D colorGradient;
    public Texture2D<float> sizeCurve;
};

public ParameterBlock<OverLifeResources> overLife;

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

    float age = max(particle.duration - particle.lifetime, 0.0);
    float age01 = particle.duration > 0.0 ? saturate(age / particle.duration) : 0.0;

    // Size over life: the baked size curve (flag bit2) replaces the simulate
    // pass's end-scale lerp; it multiplies the spawn scale.
    float2 scale = particle.scale;
    if ((emitter.flags & 4u) != 0u)
    {
        scale = particle.startScale * overLife.sizeCurve.Sample(samplers.linearClamp, float2(age01, 0.5));
    }

    // Velocity stretch (flag bit3, with align-rotation-to-velocity): the quad's
    // local x axis follows the current velocity, lengthened by the speed; the
    // perpendicular keeps the base size. Speed ~ 0 keeps the aligned rotation.
    float2 corner;
    if ((emitter.flags & 8u) != 0u)
    {
        float speed = length(particle.velocity);
        float2 axis = speed > 1e-4
            ? particle.velocity / speed
            : float2(cos(particle.rotation), sin(particle.rotation));
        float along = scale.x * emitter.overLife.y + speed * emitter.speed.w;
        corner = axis * (input.position.x * along)
            + float2(-axis.y, axis.x) * (input.position.y * scale.y);
    }
    else
    {
        corner = particleRotate(input.position.xy * scale, particle.rotation);
    }
    float3 world = mul(pc.model, float4(particle.position + corner, 0.0, 1.0)).xyz;
    output.position = mul(camera.viewProjection, float4(world, 1.0));

    // Flipbook: the frame follows the particle's age.
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

    // Color over life: the baked gradient (flag bit1) replaces the simulate
    // pass's spawn->end lerp (spawn color x gradient sample); the fade ramps
    // re-apply on top, unchanged.
    float4 color = particle.color;
    if ((emitter.flags & 2u) != 0u)
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
