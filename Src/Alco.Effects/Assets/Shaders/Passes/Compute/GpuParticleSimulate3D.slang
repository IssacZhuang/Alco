// file "GpuParticleSimulate3D.slang" - module "GpuParticleSimulate3D"
//
// 3D GPU particle simulate + compaction pass template; the 3D counterpart of
// GpuParticleSimulate2D (see its header comment for the batched work-block
// dispatch).

#language slang 2025
module GpuParticleSimulate3D;

import AlcoParticles_Core2D; // shared GpuDrawArgs + GpuWorkBlock
import AlcoParticles_Core3D;

public struct SimulateResources
{
    public RWStructuredBuffer<ParticleData3D> particles;
    public RWStructuredBuffer<EmitterParams3D> emitters;
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
public void mainCS<T : IParticleBehavior3D>(uint3 groupId : SV_GroupID, uint3 groupThread : SV_GroupThreadID)
{
    GpuWorkBlock work = resources.workBlocks[pc.workBlockBase + groupId.x];
    uint tid = work.threadBase + groupThread.x;
    EmitterParams3D emitter = resources.emitters[work.emitterSlot];
    if (tid >= emitter.capacity)
    {
        return;
    }
    uint slot = emitter.sliceOffset + tid;
    ParticleData3D particle = resources.particles[slot];
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
