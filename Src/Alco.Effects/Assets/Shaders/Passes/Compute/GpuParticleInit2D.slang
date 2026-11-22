// file "GpuParticleInit2D.slang" - module "GpuParticleInit2D"
//
// 2D GPU particle slice init pass: marks every slot of a pool slice dead
// (lifetime = -1.0) when a slice is allocated or returned.

#language slang 2025
module GpuParticleInit2D;

import Core;
import AlcoParticles_Core2D;

[GPURoot]
public struct InitRoot2D
{
    public GPUBufferView particles;
    public uint sliceOffset;
    public uint count;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    InitRoot2D root = gpu_load_root<InitRoot2D>();
    if (dispatchId.x >= root.count)
    {
        return;
    }

    ParticleData2D dead = (ParticleData2D)0;
    dead.lifetime = -1.0;
    gpu_store<ParticleData2D>(root.particles, root.sliceOffset + dispatchId.x, dead);
}
