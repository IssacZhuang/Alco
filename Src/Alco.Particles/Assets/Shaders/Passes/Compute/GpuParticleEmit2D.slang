// file "GpuParticleEmit2D.slang" - module "GpuParticleEmit2D"
//
// 2D GPU particle emit pass template: spawns this frame's new particles into
// the emitter's ring-buffer slice, generic over the group's simulation behavior
// (composed at link time — see MaterialCompiler.ComposeCompute). The frame's
// active groups are batched per behavior material into one wide dispatch: each
// 64-thread block looks up its group's work record in the work-block table
// (GpuWorkBlock) instead of a per-group push constant. The first thread of each
// group's span also resets the group's indirect draw args record of this frame,
// so every active group contributes at least one block, even with zero spawns.

#language slang 2025
module GpuParticleEmit2D;

import AlcoParticles_Core2D;

public struct EmitResources
{
    public RWStructuredBuffer<ParticleData2D> particles;
    public RWStructuredBuffer<EmitterParams2D> emitters;
    public RWStructuredBuffer<uint> renderList;
    public RWStructuredBuffer<GpuDrawArgs> drawArgs;
    public RWStructuredBuffer<GpuWorkBlock> workBlocks;
};

public ParameterBlock<EmitResources> resources;

public struct EmitConstant
{
    public uint workBlockBase; // the bucket's first record in the work-block table
    public uint pad0;
    public uint pad1;
    public uint pad2;
};

[[vk::push_constant]] EmitConstant pc;

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS<T : IParticleBehavior2D>(uint3 groupId : SV_GroupID, uint3 groupThread : SV_GroupThreadID)
{
    GpuWorkBlock work = resources.workBlocks[pc.workBlockBase + groupId.x];
    uint tid = work.threadBase + groupThread.x;
    EmitterParams2D emitter = resources.emitters[work.emitterSlot];
    if (tid == 0)
    {
        // The args record lands at the CPU-assigned drawIndex of the material-
        // batched array (contiguous per material for the multi-draw), and its
        // firstInstance addresses the draw's run in the instance-data buffer.
        GpuDrawArgs args;
        args.indexCount = emitter.renderMisc.x;
        args.instanceCount = 0;
        args.firstIndex = 0;
        args.vertexOffset = 0;
        args.firstInstance = work.drawStart;
        resources.drawArgs[work.drawIndex] = args;
    }
    if (tid >= emitter.spawnCount)
    {
        return;
    }
    uint local = (emitter.emitCursor + tid) % emitter.capacity;
    uint slot = emitter.sliceOffset + local;
    uint rng = particleHash(emitter.frameSeed, slot);
    ParticleData2D particle = (ParticleData2D)0;
    T behavior = T();
    behavior.spawn(particle, emitter, rng);
    resources.particles[slot] = particle;
}
