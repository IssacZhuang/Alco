// file "GpuParticle2D.slang" - module "GpuParticle2D"
//
// The built-in 2D particle render pass TEMPLATE: draws the emitter's quad mesh
// once per simulated particle (batched indexed-indirect — all draws of one
// material merge into a single multi-draw, the per-draw identity traveling
// through the instance-step "drawData" record the simulate pass writes),
// expanding the quad in the vertex shader from the particle record. Both
// entries are generic over the group's particle surface (IParticleSurface,
// AlcoParticles_Surface — the dimension-neutral contract shared with the 3D
// pass): the material compiler composes this module with the group's material
// surface module, so the visual comes from the .amat — the built-in default
// surface (ParticleSurfaceDefault) shades texture x particle color and
// leaves the vertex stage untouched (the adjustWorldPosition hook's default).
//
// The pass owns the resource contract the particle system binds: the "camera"
// block (auto-bound view-projection), the "scene" block (bound by the particle
// system) and the "overLife" block (the group's baked color-gradient/size-curve
// lookup textures). The surface module owns the material resources, and shared
// resources beyond the pass (e.g. a lighting middleware's light map) bind into
// slots the surface declares through material modules (IParticleMaterialModule)
// - this pass stays agnostic of them. There is
// no push constant: the emitter slot comes from the drawData record and the
// emitter transform from the emitter parameters (worldMatrix; world-space
// groups draw through identity).

#language slang 2025
module GpuParticle2D;

import AlcoRendering_Core;
import AlcoParticles_Core2D;
import AlcoParticles_Surface;

public struct Vertex
{
    public float3 position : POSITION;
    public float2 uv : TEXCOORD0;
    // The per-draw instance record (GpuInstanceData) the simulate pass wrote:
    // x = emitter slot, y = the particle's slot inside the emitter's slice. The
    // field name routes it to the instance-step vertex buffer (vertex slot 1),
    // fetched through each indirect record's firstInstance — no instance-id
    // builtin, whose firstInstance semantics differ between Vulkan and D3D12.
    public uint2 drawData : TEXCOORD2;
};

public struct V2F
{
    public float4 position : SV_POSITION;
    public float2 uv : TEXCOORD0;
    public float4 color : COLOR0;
    public float age01 : TEXCOORD1;
    public float3 worldPosition : TEXCOORD2; // the shaded fragment's world position (post vertex hook)
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

[shader("vertex")]
public V2F MainVS<T : IParticleSurface>(Vertex input)
{
    V2F output = (V2F)0;
    EmitterParams2D emitter = scene.emitters[input.drawData.x];
    ParticleData2D particle = scene.particles[emitter.sliceOffset + input.drawData.y];

    // The draw's emitter transform: local-space groups draw through the
    // emitter's per-frame worldMatrix, world-space groups through identity.
    float4x4 model = emitter.worldMatrix;
    if ((emitter.flags & PARTICLE_FLAG_WORLD_SPACE) != 0u)
    {
        model = float4x4(
            float4(1.0, 0.0, 0.0, 0.0),
            float4(0.0, 1.0, 0.0, 0.0),
            float4(0.0, 0.0, 1.0, 0.0),
            float4(0.0, 0.0, 0.0, 1.0));
    }

    float age = max(particle.duration - particle.lifetime, 0.0);
    float age01 = particle.duration > 0.0 ? saturate(age / particle.duration) : 0.0;

    // Size over life: the baked size curve (PARTICLE_FLAG_SIZE_CURVE) replaces
    // the simulate pass's end-scale lerp; it multiplies the spawn scale.
    float2 scale = particle.scale;
    if ((emitter.flags & PARTICLE_FLAG_SIZE_CURVE) != 0u)
    {
        scale = particle.startScale * overLife.sizeCurve.Sample(samplers.linearClamp, float2(age01, 0.5));
    }

    // Velocity stretch (PARTICLE_FLAG_VELOCITY_STRETCH, with
    // align-rotation-to-velocity): the quad's
    // local x axis follows the current velocity, lengthened by the speed; the
    // perpendicular keeps the base size. Speed ~ 0 keeps the aligned rotation.
    float2 corner;
    if ((emitter.flags & PARTICLE_FLAG_VELOCITY_STRETCH) != 0u)
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
    float height = particle.height;
    if ((emitter.flags & PARTICLE_FLAG_WORLD_SPACE) == 0u)
    {
        height += emitter.emitterHeight;
    }
    float3 world = mul(model, float4(particle.position + corner, 0.0, 1.0)).xyz;
    float3 particleWorld = mul(model, float4(particle.position, 0.0, 1.0)).xyz;
    world.y += height;
    particleWorld.y += height;

    // Scene-depth arbitration (PARTICLE_FLAG_DEPTH_BASE): the group's depth base
    // minus the particle's height becomes world z — height recedes the particle
    // in depth, matching entity depth stacks — so a group depth-testing the scene
    // ("depth": "Read") sorts against it. Surface vertex hooks run after this and
    // only refine the projection (e.g. tilt a standing facade quad); groups
    // without the flag keep z = 0.
    if ((emitter.flags & PARTICLE_FLAG_DEPTH_BASE) != 0u)
    {
        float depth = emitter.depthBase - height;
        world.z = depth;
        particleWorld.z = depth;
    }

    // The material's vertex-stage hook (e.g. a facade quad's depth tilt, on top
    // of the depth-base z above), fed through the
    // dimension-neutral ParticleVertexInput: the default surface returns the
    // position unchanged.
    T surface = T();
    ParticleVertexInput vertexInput;
    vertexInput.world = world;
    vertexInput.particlePosition = particleWorld;
    vertexInput.age01 = age01;
    vertexInput.emitterWorldMatrix = emitter.worldMatrix;
    vertexInput.customData = emitter.customData;
    vertexInput.height = height;
    world = surface.adjustWorldPosition(vertexInput);
    output.worldPosition = world;
    output.position = mul(camera.viewProjection, float4(world, 1.0));

    // Flipbook: the playback rate is relative to the particle's lifetime —
    // flipbook.z plays the anim that many times over one lifetime (1 = every
    // frame exactly once from spawn to death, per particle; 2 = twice; 0 =
    // static first frame). flipbook.w frames per anim splits the sheet into
    // variants; each particle plays the one anim picked at spawn
    // (particle.animStart). PARTICLE_FLAG_FLIPBOOK_REVERSE plays the anim
    // backwards (last frame at spawn — sprite sheets authored for
    // lifetime-keyed playback).
    float2 uv = input.uv;
    uint rows = uint(emitter.flipbook.x);
    uint cols = uint(emitter.flipbook.y);
    if (rows * cols > 1u)
    {
        uint total = rows * cols;
        uint perAnim = emitter.flipbook.w > 0.5 ? min(uint(emitter.flipbook.w), total) : total;
        uint frameInAnim = uint(age01 * float(perAnim) * emitter.flipbook.z) % perAnim;
        if ((emitter.flags & PARTICLE_FLAG_FLIPBOOK_REVERSE) != 0u)
        {
            frameInAnim = perAnim - 1u - frameInAnim;
        }
        uint frame = particle.animStart + frameInAnim;
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
    surfaceInput.worldPosition = input.worldPosition;
    return surface.shade(surfaceInput);
}
