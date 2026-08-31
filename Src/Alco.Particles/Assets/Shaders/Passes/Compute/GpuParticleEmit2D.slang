// file "GpuParticleEmit2D.slang" - module "GpuParticleEmit2D"
//
// 2D GPU particle emit pass template: spawns this frame's new particles into
// the emitter's ring-buffer slice, generic over the group's simulation behavior
// (composed at link time — see MaterialCompiler.ComposeCompute). Thread 0 also
// resets the emitter's indirect draw args record of this frame, so the pass is
// dispatched every frame the emitter is active, even with zero spawns.

#language slang 2025
module GpuParticleEmit2D;

import AlcoParticles_Core2D;

public struct EmitResources
{
    public RWStructuredBuffer<ParticleData2D> particles;
    public RWStructuredBuffer<EmitterParams2D> emitters;
    public RWStructuredBuffer<uint> renderList;
    public RWStructuredBuffer<GpuDrawArgs> drawArgs;
};

public ParameterBlock<EmitResources> resources;

public struct EmitConstant
{
    public uint emitterSlot;
    public uint drawIndex;   // the record's slot in this frame's draw-args array
    public uint drawStart;   // the draw's firstInstance / instance-data base
    public uint pad0;
};

[[vk::push_constant]] EmitConstant pc;

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS<T : IParticleBehavior2D>(uint3 dispatchId : SV_DispatchThreadID)
{
    EmitterParams2D emitter = resources.emitters[pc.emitterSlot];
    if (dispatchId.x == 0)
    {
        // The args record lands at the CPU-assigned drawIndex of the material-
        // batched array (contiguous per material for the multi-draw), and its
        // firstInstance addresses the draw's run in the instance-data buffer.
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
    ParticleData2D particle = (ParticleData2D)0;
    T behavior = T();
    behavior.spawn(particle, emitter, rng);
    resources.particles[slot] = particle;
}
