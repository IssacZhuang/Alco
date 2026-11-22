// file "GpuParticleResetArgs.slang" - module "GpuParticleResetArgs"
//
// Resets this frame's per-batch indirect draw records to non-indexed quad
// defaults (vertexCount = 6, instanceCount = 0, firstVertex = 0, firstInstance = 0)
// before GpuParticleSimulate increments instanceCount atomically.

#language slang 2025
module GpuParticleResetArgs;

import Core;
import AlcoParticles_Core2D;

[GPURoot]
public struct ResetArgsRoot
{
    public GPUBufferView drawArgs;
    public uint drawCount;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    ResetArgsRoot root = gpu_load_root<ResetArgsRoot>();
    if (dispatchId.x >= root.drawCount)
    {
        return;
    }

    GpuDrawArgs args;
    args.vertexCount = 6u;
    args.instanceCount = 0u;
    args.firstVertex = 0u;
    args.firstInstance = 0u;
    gpu_store<GpuDrawArgs>(root.drawArgs, dispatchId.x, args);
}
