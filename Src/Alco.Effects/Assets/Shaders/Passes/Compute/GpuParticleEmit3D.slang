// file "GpuParticleEmit3D.slang" - module "GpuParticleEmit3D"
//
// 3D GPU particle emit pass template: spawns this frame's new particles into
// the emitter's ring-buffer slice, generic over IParticleBehavior3D.

#language slang 2025
module GpuParticleEmit3D;

import Core;
import AlcoParticles_Core2D;
import AlcoParticles_Core3D;

[GPURoot]
public struct EmitRoot3D
{
    public GPUBufferView particles;
    public GPUBufferView emitters;
    public GPUBufferView workBlocks;
    public uint workBlockBase;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS<T : IParticleBehavior3D>(uint3 groupId : SV_GroupID, uint3 groupThread : SV_GroupThreadID)
{
    EmitRoot3D root = gpu_load_root<EmitRoot3D>();
    GpuWorkBlock work = gpu_load<GpuWorkBlock>(root.workBlocks, root.workBlockBase + groupId.x);
    uint tid = work.threadBase + groupThread.x;
    EmitterParams3D emitter = gpu_load<EmitterParams3D>(root.emitters, work.emitterSlot);
    if (tid >= emitter.spawnCount)
    {
        return;
    }

    uint local = (emitter.emitCursor + tid) % emitter.capacity;
    uint slot = emitter.sliceOffset + local;
    uint rng = particleHash(emitter.frameSeed, slot);
    ParticleData3D particle = (ParticleData3D)0;
    T behavior = T();
    behavior.spawn(particle, emitter, rng);
    gpu_store<ParticleData3D>(root.particles, slot, particle);
}
