// file "GpuParticleInit3D.slang" - module "GpuParticleInit3D"
//
// 3D GPU particle slice init pass; the 3D counterpart of GpuParticleInit2D
// (see its header comment).

#language slang 2025
module GpuParticleInit3D;

import AlcoParticles_Core3D;

public struct InitResources
{
    public RWStructuredBuffer<ParticleData3D> particles;
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
