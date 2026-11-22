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

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS<T : IParticleBehavior2D>(uint3 groupId : SV_GroupID, uint3 groupThread : SV_GroupThreadID)
{
    SimulateRoot2D root = gpu_load_root<SimulateRoot2D>();
    GpuWorkBlock work = gpu_load<GpuWorkBlock>(root.workBlocks, root.workBlockBase + groupId.x);
    uint tid = work.threadBase + groupThread.x;
    EmitterParams2D emitter = gpu_load<EmitterParams2D>(root.emitters, work.emitterSlot);
    if (tid >= emitter.capacity)
    {
        return;
    }

    uint slot = emitter.sliceOffset + tid;
    ParticleData2D particle = gpu_load<ParticleData2D>(root.particles, slot);
    if (particle.lifetime <= 0.0)
    {
        return;
    }

    T behavior = T();
    behavior.simulate(particle, emitter);
    gpu_store<ParticleData2D>(root.particles, slot, particle);
    if (particle.lifetime <= 0.0)
    {
        return;
    }

    // Offset 4 inside GpuDrawArgs (16 bytes) is instanceCount.
    uint target = gpu_atomic_add_u32(gpu_offset_bytes(root.drawArgs, work.drawIndex * 16u + 4u), 1u);
    GpuInstanceData instance;
    instance.emitterSlot = work.emitterSlot;
    instance.particleIndex = tid;
    gpu_store<GpuInstanceData>(root.instanceData, work.drawStart + target, instance);
}
