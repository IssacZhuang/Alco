// file "GpuParticleInit2D.slang" - module "GpuParticleInit2D"
//
// 2D GPU particle slice init pass: marks every slot of a pool slice dead
// (lifetime < 0). Dispatched once when a slice is allocated or returned, so a
// new tenant never sees a previous tenant's particles.

#language slang 2025
module GpuParticleInit2D;

import AlcoParticles_Core2D;

public struct InitResources
{
    public RWStructuredBuffer<ParticleData2D> particles;
};

public ParameterBlock<InitResources> resources;

public struct InitConstant
{
    public uint sliceOffset;
    public uint count;
    public uint pad0;
    public uint pad1;
};

[[vk::push_constant]] InitConstant pc;

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    if (dispatchId.x >= pc.count)
    {
        return;
    }
    resources.particles[pc.sliceOffset + dispatchId.x].lifetime = -1.0;
}
