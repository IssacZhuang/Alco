// file "GpuParticleSimulate2D.slang" - module "GpuParticleSimulate2D"
//
// 2D GPU particle simulate + compaction pass template: advances live particles
// generic over IParticleBehavior2D and appends surviving (emitterSlot, particleIndex)
// records into the batch's instance range while atomically incrementing drawArgs.instanceCount.

#language slang 2025
module GpuParticleSimulate2D;

import Core;
import AlcoParticles_Core2D;

[GPURoot]
public struct SimulateRoot2D
{
    public GPUBufferView particles;
    public GPUBufferView emitters;
    public GPUBufferView drawArgs;
    public GPUBufferView instanceData;
    public GPUBufferView workBlocks;
    public uint workBlockBase;
};

groupshared uint sGroupCount;
groupshared uint sGroupBase;

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS<T : IParticleBehavior2D>(uint3 groupId : SV_GroupID, uint3 groupThread : SV_GroupThreadID)
{
    if (groupThread.x == 0u)
    {
        sGroupCount = 0u;
    }

    SimulateRoot2D root = gpu_load_root<SimulateRoot2D>();
    GpuWorkBlock work = gpu_load<GpuWorkBlock>(root.workBlocks, root.workBlockBase + groupId.x);
    uint tid = work.threadBase + groupThread.x;
    EmitterParams2D emitter = gpu_load<EmitterParams2D>(root.emitters, work.emitterSlot);

    bool alive = false;
    if (tid < emitter.capacity)
    {
        uint slot = emitter.sliceOffset + tid;
        ParticleData2D particle = gpu_load<ParticleData2D>(root.particles, slot);
        if (particle.lifetime > 0.0)
        {
            T behavior = T();
            behavior.simulate(particle, emitter);
            gpu_store<ParticleData2D>(root.particles, slot, particle);
            alive = particle.lifetime > 0.0;
        }
    }

    GroupMemoryBarrierWithGroupSync();

    uint localOffset = 0u;
    if (alive)
    {
        InterlockedAdd(sGroupCount, 1u, localOffset);
    }
    GroupMemoryBarrierWithGroupSync();

    if (groupThread.x == 0u && sGroupCount > 0u)
    {
        // Offset 4 inside GpuDrawArgs (20 bytes) is instanceCount.
        sGroupBase = gpu_atomic_add_u32(gpu_offset_bytes(root.drawArgs, work.drawIndex * 20u + 4u), sGroupCount);
    }
    GroupMemoryBarrierWithGroupSync();

    if (alive)
    {
        GpuInstanceData instance;
        instance.emitterSlot = work.emitterSlot;
        instance.particleIndex = tid;
        gpu_store<GpuInstanceData>(root.instanceData, work.drawStart + sGroupBase + localOffset, instance);
    }
}
