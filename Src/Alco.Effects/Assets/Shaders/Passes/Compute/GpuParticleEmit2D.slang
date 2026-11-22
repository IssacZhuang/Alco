// file "GpuParticleEmit2D.slang" - module "GpuParticleEmit2D"
//
// 2D GPU particle emit pass template: spawns this frame's new particles into
// the emitter's ring-buffer slice, generic over IParticleBehavior2D.

#language slang 2025
module GpuParticleEmit2D;

import Core;
import AlcoParticles_Core2D;

[GPURoot]
public struct EmitRoot2D
{
    public GPUBufferView particles;
    public GPUBufferView emitters;
    public GPUBufferView workBlocks;
    public uint workBlockBase;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS<T : IParticleBehavior2D>(uint3 groupId : SV_GroupID, uint3 groupThread : SV_GroupThreadID)
{
    EmitRoot2D root = gpu_load_root<EmitRoot2D>();
    GpuWorkBlock work = gpu_load<GpuWorkBlock>(root.workBlocks, root.workBlockBase + groupId.x);
    uint tid = work.threadBase + groupThread.x;
    EmitterParams2D emitter = gpu_load<EmitterParams2D>(root.emitters, work.emitterSlot);
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
    gpu_store<ParticleData2D>(root.particles, slot, particle);
}
