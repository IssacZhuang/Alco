// file "GpuParticle2D.slang" - module "GpuParticle2D"
//
// The built-in 2D particle render pass TEMPLATE: draws surviving particles via
// indexed indirect instancing (4 vertices / 6 indices per quad x instanceCount),
// composed at link time with an IParticleSurface module.

#language slang 2025
module GpuParticle2D;

import Core;
import SharedSampler;
import AlcoParticles_Core2D;
import AlcoParticles_Surface;

public struct ParticlePassData2D
{
    public float4x4 viewProjection;
    public GPUBufferView particles;
    public GPUBufferView emitters;
    public GPUBufferView instanceData;
};

[GPURoot]
public struct ParticlePassRoot2D<T : IParticleSurface>
{
    public ParticlePassData2D Pass;
    public T.Params Material;
};

public struct V2F
{
    public float4 position : SV_POSITION;
    public float2 uv : TEXCOORD0;
    public float4 color : COLOR0;
    public float age01 : TEXCOORD1;
    public float3 worldPosition : TEXCOORD2;
    public nointerpolation uint spriteTextureId : TEXCOORD3;
};

[shader("vertex")]
public V2F MainVS<T : IParticleSurface>(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    ParticlePassRoot2D<T> root = gpu_load_root<ParticlePassRoot2D<T>>();
    GpuInstanceData inst = gpu_load<GpuInstanceData>(root.Pass.instanceData, instanceId);
    EmitterParams2D emitter = gpu_load<EmitterParams2D>(root.Pass.emitters, inst.emitterSlot);
    ParticleData2D particle = gpu_load<ParticleData2D>(root.Pass.particles, emitter.sliceOffset + inst.particleIndex);

    float2 cornerPos;
    float2 quadUv;
    particleQuadVertex(vertexId, cornerPos, quadUv);

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

    float2 scale = particle.scale;
    if ((emitter.flags & PARTICLE_FLAG_SIZE_CURVE) != 0u)
    {
        float curve = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(emitter.sizeCurveTextureId))
            .SampleLevel(shared_sampler_linear_clamp(), float2(age01, 0.5), 0.0).r;
        scale = particle.startScale * curve;
    }

    float2 corner;
    if ((emitter.flags & PARTICLE_FLAG_VELOCITY_STRETCH) != 0u)
    {
        float speed = length(particle.velocity);
        float2 axis = speed > 1e-4
            ? particle.velocity / speed
            : float2(cos(particle.rotation), sin(particle.rotation));
        float along = scale.x * emitter.overLife.y + speed * emitter.speed.w;
        corner = axis * (cornerPos.x * along)
            + float2(-axis.y, axis.x) * (cornerPos.y * scale.y);
    }
    else
    {
        corner = particleRotate(cornerPos * scale, particle.rotation);
    }

    float height = particle.height;
    if ((emitter.flags & PARTICLE_FLAG_WORLD_SPACE) == 0u)
    {
        height += emitter.emitterHeight;
    }

    float3 world = mul(float4(particle.position + corner, 0.0, 1.0), model).xyz;
    float3 particleWorld = mul(float4(particle.position, 0.0, 1.0), model).xyz;
    world.y += height;
    particleWorld.y += height;

    if ((emitter.flags & PARTICLE_FLAG_DEPTH_BASE) != 0u)
    {
        float depth = emitter.depthBase - height;
        world.z = depth;
        particleWorld.z = depth;
    }

    T surface = T();
    ParticleVertexInput vertexInput;
    vertexInput.world = world;
    vertexInput.particlePosition = particleWorld;
    vertexInput.age01 = age01;
    vertexInput.emitterWorldMatrix = emitter.worldMatrix;
    vertexInput.customData = emitter.customData;
    vertexInput.height = height;
    world = surface.getWorldPosition(vertexInput, root.Material);

    V2F output = (V2F)0;
    output.worldPosition = world;
    output.position = mul(float4(world, 1.0), root.Pass.viewProjection);

    float2 uv = quadUv;
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

    float4 color = particle.color;
    if ((emitter.flags & PARTICLE_FLAG_COLOR_GRADIENT) != 0u)
    {
        color = particle.startColor * gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(emitter.colorGradientTextureId))
            .SampleLevel(shared_sampler_linear_clamp(), float2(age01, 0.5), 0.0);
        float fadeIn = emitter.life.z > 1e-5 ? smoothstep(0.0, emitter.life.z, age01) : 1.0;
        float fadeOut = emitter.life.w > 1e-5 ? 1.0 - smoothstep(1.0 - emitter.life.w, 1.0, age01) : 1.0;
        color.a *= fadeIn * fadeOut;
    }
    output.color = color * emitter.tint;
    output.age01 = age01;
    output.spriteTextureId = emitter.spriteTextureId;
    return output;
}

[shader("fragment")]
public float4 MainPS<T : IParticleSurface>(V2F input) : SV_TARGET
{
    ParticlePassRoot2D<T> root = gpu_load_root<ParticlePassRoot2D<T>>();
    T surface = T();
    ParticleSurfaceInput surfaceInput;
    surfaceInput.uv = input.uv;
    surfaceInput.color = input.color;
    surfaceInput.age01 = input.age01;
    surfaceInput.worldPosition = input.worldPosition;
    surfaceInput.spriteTexture.index = input.spriteTextureId;
    return surface.getColor(surfaceInput, root.Material);
}
