// file "GpuParticleSimulate3D.slang" - module "GpuParticleSimulate3D"
//
// 3D GPU particle simulate + compaction pass template; the 3D counterpart of
// GpuParticleSimulate2D (see its header comment).

#language slang 2025
module GpuParticleSimulate3D;

import AlcoParticles_Core2D; // shared GpuDrawArgs
import AlcoParticles_Core3D;

public struct SimulateResources
{
    public RWStructuredBuffer<ParticleData3D> particles;
    public RWStructuredBuffer<EmitterParams3D> emitters;
    public RWStructuredBuffer<uint> renderList;
    public RWStructuredBuffer<GpuDrawArgs> drawArgs;
    public RWStructuredBuffer<GpuInstanceData> instanceData;
};

public ParameterBlock<SimulateResources> resources;

public struct SimulateConstant
{
    public uint emitterSlot;
    public uint drawIndex;
    public uint drawStart;
    public uint pad0;
};

[[vk::push_constant]] SimulateConstant pc;

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS<T : IParticleBehavior3D>(uint3 dispatchId : SV_DispatchThreadID)
{
    EmitterParams3D emitter = resources.emitters[pc.emitterSlot];
    if (dispatchId.x >= emitter.capacity)
    {
        return;
    }
    uint slot = emitter.sliceOffset + dispatchId.x;
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
    InterlockedAdd(resources.drawArgs[pc.drawIndex].instanceCount, 1, target);
    resources.renderList[emitter.sliceOffset + target] = dispatchId.x;
    GpuInstanceData instance;
    instance.emitterSlot = pc.emitterSlot;
    instance.particleIndex = dispatchId.x;
    resources.instanceData[pc.drawStart + target] = instance;
}
