// file "GpuParticleEmit3D.slang" - module "GpuParticleEmit3D"
//
// 3D GPU particle emit pass template; the 3D counterpart of GpuParticleEmit2D
// (see its header comment).

#language slang 2025
module GpuParticleEmit3D;

import AlcoParticles_Core2D; // shared GpuDrawArgs + hash RNG
import AlcoParticles_Core3D;

public struct EmitResources
{
    public RWStructuredBuffer<ParticleData3D> particles;
    public RWStructuredBuffer<EmitterParams3D> emitters;
    public RWStructuredBuffer<uint> renderList;
    public RWStructuredBuffer<GpuDrawArgs> drawArgs;
};

public ParameterBlock<EmitResources> resources;

public struct EmitConstant
{
    public uint emitterSlot;
    public uint drawIndex;
    public uint drawStart;
    public uint pad0;
};

[[vk::push_constant]] EmitConstant pc;

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS<T : IParticleBehavior3D>(uint3 dispatchId : SV_DispatchThreadID)
{
    EmitterParams3D emitter = resources.emitters[pc.emitterSlot];
    if (dispatchId.x == 0)
    {
        // See GpuParticleEmit2D: the record lands at the CPU-assigned drawIndex
        // of the material-batched args array, firstInstance addressing the
        // draw's run in the instance-data buffer.
        GpuDrawArgs args;
        args.indexCount = emitter.renderMisc.x;
        args.instanceCount = 0;
        args.firstIndex = 0;
        args.vertexOffset = 0;
        args.firstInstance = pc.drawStart;
        resources.drawArgs[pc.drawIndex] = args;
    }
    if (dispatchId.x >= emitter.spawnCount)
    {
        return;
    }
    uint local = (emitter.emitCursor + dispatchId.x) % emitter.capacity;
    uint slot = emitter.sliceOffset + local;
    uint rng = particleHash(emitter.frameSeed, slot);
    ParticleData3D particle = (ParticleData3D)0;
    T behavior = T();
    behavior.spawn(particle, emitter, rng);
    resources.particles[slot] = particle;
}
