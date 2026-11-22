// file "GpuParticleInit3D.slang" - module "GpuParticleInit3D"
//
// 3D GPU particle slice init pass: marks every slot of a pool slice dead
// (lifetime = -1.0) when a slice is allocated or returned.

#language slang 2025
module GpuParticleInit3D;

import Core;
import AlcoParticles_Core3D;

[GPURoot]
public struct InitRoot3D
{
    public GPUBufferView particles;
    public uint sliceOffset;
    public uint count;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    InitRoot3D root = gpu_load_root<InitRoot3D>();
    if (dispatchId.x >= root.count)
    {
        return;
    }

    ParticleData3D dead = (ParticleData3D)0;
    dead.lifetime = -1.0;
    gpu_store<ParticleData3D>(root.particles, root.sliceOffset + dispatchId.x, dead);
}
