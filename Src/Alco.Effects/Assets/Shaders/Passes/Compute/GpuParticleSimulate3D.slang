// file "GpuParticleSimulate3D.slang" - module "GpuParticleSimulate3D"
//
// 3D GPU particle simulate + compaction pass template; 3D counterpart of GpuParticleSimulate2D.

#language slang 2025
module GpuParticleSimulate3D;

import Core;
import AlcoParticles_Core2D;
import AlcoParticles_Core3D;

[GPURoot]
public struct SimulateRoot3D
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
public void mainCS<T : IParticleBehavior3D>(uint3 groupId : SV_GroupID, uint3 groupThread : SV_GroupThreadID)
{
    if (groupThread.x == 0u)
    {
        sGroupCount = 0u;
    }

    SimulateRoot3D root = gpu_load_root<SimulateRoot3D>();
    GpuWorkBlock work = gpu_load<GpuWorkBlock>(root.workBlocks, root.workBlockBase + groupId.x);
    uint tid = work.threadBase + groupThread.x;
    EmitterParams3D emitter = gpu_load<EmitterParams3D>(root.emitters, work.emitterSlot);

    bool alive = false;
    if (tid < emitter.capacity)
    {
        uint slot = emitter.sliceOffset + tid;
        ParticleData3D particle = gpu_load<ParticleData3D>(root.particles, slot);
        if (particle.lifetime > 0.0)
        {
            T behavior = T();
            behavior.simulate(particle, emitter);
            gpu_store<ParticleData3D>(root.particles, slot, particle);
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
