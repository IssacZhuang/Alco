// file "GpuParticleSimulate2D.slang" - module "GpuParticleSimulate2D"
//
// 2D GPU particle simulate + compaction pass template: advances every live
// particle of the emitter's slice by the frame's delta time (generic over the
// group's simulation behavior) and appends the survivors to the emitter's
// render list, atomically counting them into the indirect draw args record the
// render pass draws with. Batched like the emit pass: one wide dispatch per
// behavior material, each 64-thread block resolving its group through the
// work-block table (see GpuParticleEmit2D).

#language slang 2025
module GpuParticleSimulate2D;

import AlcoParticles_Core2D;

public struct SimulateResources
{
    public RWStructuredBuffer<ParticleData2D> particles;
    public RWStructuredBuffer<EmitterParams2D> emitters;
    public RWStructuredBuffer<uint> renderList;
    public RWStructuredBuffer<GpuDrawArgs> drawArgs;
    public RWStructuredBuffer<GpuInstanceData> instanceData;
    public RWStructuredBuffer<GpuWorkBlock> workBlocks;
};

public ParameterBlock<SimulateResources> resources;

public struct SimulateConstant
{
    public uint workBlockBase; // the bucket's first record in the work-block table
    public uint pad0;
    public uint pad1;
    public uint pad2;
};

[[vk::push_constant]] SimulateConstant pc;

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS<T : IParticleBehavior2D>(uint3 groupId : SV_GroupID, uint3 groupThread : SV_GroupThreadID)
{
    GpuWorkBlock work = resources.workBlocks[pc.workBlockBase + groupId.x];
    uint tid = work.threadBase + groupThread.x;
    EmitterParams2D emitter = resources.emitters[work.emitterSlot];
    if (tid >= emitter.capacity)
    {
        return;
    }
    uint slot = emitter.sliceOffset + tid;
    ParticleData2D particle = resources.particles[slot];
    if (particle.lifetime <= 0.0)
    {
        return;
    }
    T behavior = T();
    behavior.simulate(particle, emitter);
    resources.particles[slot] = particle;
    if (particle.lifetime <= 0.0)
    {
        return;
    }
    uint target;
    InterlockedAdd(resources.drawArgs[work.drawIndex].instanceCount, 1, target);
    resources.renderList[emitter.sliceOffset + target] = tid;
    GpuInstanceData instance;
    instance.emitterSlot = work.emitterSlot;
    instance.particleIndex = tid;
    resources.instanceData[work.drawStart + target] = instance;
}
