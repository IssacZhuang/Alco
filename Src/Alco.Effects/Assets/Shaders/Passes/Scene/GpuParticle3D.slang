// file "GpuParticle3D.slang" - module "GpuParticle3D"
//
// The built-in 3D particle render pass TEMPLATE: camera-facing billboard quads,
// drawn via indexed indirect instancing (4 vertices / 6 indices per quad) composed with IParticleSurface.

#language slang 2025
module GpuParticle3D;

import Core;
import SharedSampler;
import AlcoParticles_Core2D;
import AlcoParticles_Core3D;
import AlcoParticles_Surface;

public struct ParticlePassData3D
{
    public float4x4 viewProjection;
    public float4 cameraRight;
    public float4 cameraUp;
    public GPUBufferView particles;
    public GPUBufferView emitters;
    public GPUBufferView instanceData;
};

[GPURoot]
public struct ParticlePassRoot3D<T : IParticleSurface>
{
    public ParticlePassData3D Pass;
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
    ParticlePassRoot3D<T> root = gpu_load_root<ParticlePassRoot3D<T>>();
    GpuInstanceData inst = gpu_load<GpuInstanceData>(root.Pass.instanceData, instanceId);
    EmitterParams3D emitter = gpu_load<EmitterParams3D>(root.Pass.emitters, inst.emitterSlot);
    ParticleData3D particle = gpu_load<ParticleData3D>(root.Pass.particles, emitter.sliceOffset + inst.particleIndex);

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

    float size = particle.size;
    if ((emitter.flags & PARTICLE_FLAG_SIZE_CURVE) != 0u)
    {
        float curve = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(emitter.sizeCurveTextureId))
            .SampleLevel(shared_sampler_linear_clamp(), float2(age01, 0.5), 0.0).r;
        size = particle.startSize * curve;
    }

    float3 center = mul(float4(particle.position, 1.0), model).xyz;
    float3 world;
    if ((emitter.flags & PARTICLE_FLAG_VELOCITY_STRETCH) != 0u)
    {
        float3 worldVelocity = mul(float4(particle.velocity, 0.0), model).xyz;
        float2 planeVelocity = float2(
            dot(worldVelocity, root.Pass.cameraRight.xyz),
            dot(worldVelocity, root.Pass.cameraUp.xyz));
        float planeSpeed = length(planeVelocity);
        if (planeSpeed > 1e-4)
        {
            float2 axis2 = planeVelocity / planeSpeed;
            float3 axis = root.Pass.cameraRight.xyz * axis2.x + root.Pass.cameraUp.xyz * axis2.y;
            float3 perp = root.Pass.cameraRight.xyz * (-axis2.y) + root.Pass.cameraUp.xyz * axis2.x;
            float along = size * emitter.size.w + length(worldVelocity) * emitter.speed.w;
            world = center + axis * (cornerPos.x * along) + perp * (cornerPos.y * size);
        }
        else
        {
            float2 corner = particleRotate(cornerPos * size, particle.rotation);
            world = center + root.Pass.cameraRight.xyz * corner.x + root.Pass.cameraUp.xyz * corner.y;
        }
    }
    else
    {
        float2 corner = particleRotate(cornerPos * size, particle.rotation);
        world = center + root.Pass.cameraRight.xyz * corner.x + root.Pass.cameraUp.xyz * corner.y;
    }

    T surface = T();
    ParticleVertexInput vertexInput;
    vertexInput.world = world;
    vertexInput.particlePosition = center;
    vertexInput.age01 = age01;
    vertexInput.emitterWorldMatrix = emitter.worldMatrix;
    vertexInput.customData = emitter.customData;
    vertexInput.height = 0.0;
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
    ParticlePassRoot3D<T> root = gpu_load_root<ParticlePassRoot3D<T>>();
    T surface = T();
    ParticleSurfaceInput surfaceInput;
    surfaceInput.uv = input.uv;
    surfaceInput.color = input.color;
    surfaceInput.age01 = input.age01;
    surfaceInput.worldPosition = input.worldPosition;
    surfaceInput.spriteTexture.index = input.spriteTextureId;
    return surface.getColor(surfaceInput, root.Material);
}
