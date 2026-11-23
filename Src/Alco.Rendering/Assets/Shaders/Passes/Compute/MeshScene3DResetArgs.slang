#language slang 2025
module MeshScene3DResetArgs;

import Core;
import AlcoRendering_MeshScene3D;

[GPURoot]
public struct MeshScene3DResetArgsRoot
{
    public GPUBufferView BucketMeta;
    public GPUBufferView DrawArgs;
    public uint BucketCount;
    public uint Pad0;
};

struct DrawIndexedIndirectArgs
{
    uint IndexCount;
    uint InstanceCount;
    uint FirstIndex;
    int VertexOffset;
    uint FirstInstance;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS(uint3 id : SV_DispatchThreadID)
{
    MeshScene3DResetArgsRoot root = gpu_load_root<MeshScene3DResetArgsRoot>();
    if (id.x >= root.BucketCount)
    {
        return;
    }

    MeshScene3DBucketMeta meta = gpu_load<MeshScene3DBucketMeta>(root.BucketMeta, id.x);
    DrawIndexedIndirectArgs args;
    args.IndexCount = meta.IndexCount;
    args.InstanceCount = 0u;
    args.FirstIndex = meta.FirstIndex;
    args.VertexOffset = meta.VertexOffset;
    args.FirstInstance = 0u;
    gpu_store<DrawIndexedIndirectArgs>(root.DrawArgs, id.x, args);
}
