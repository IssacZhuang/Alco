#language slang 2025
module SpriteSceneResetArgs;

import Core;
import AlcoRendering_SpriteScene;

[GPURoot]
public struct SpriteSceneResetArgsRoot
{
    public GPUBufferView BucketMeta;
    public GPUBufferView DrawArgs;
    public uint BucketCount;
    public uint Pad0;
};

struct DrawIndirectArgs
{
    uint VertexCount;
    uint InstanceCount;
    uint FirstVertex;
    uint FirstInstance;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS(uint3 id : SV_DispatchThreadID)
{
    SpriteSceneResetArgsRoot root = gpu_load_root<SpriteSceneResetArgsRoot>();
    if (id.x >= root.BucketCount)
    {
        return;
    }

    SpriteSceneBucketMeta meta = gpu_load<SpriteSceneBucketMeta>(root.BucketMeta, id.x);
    DrawIndirectArgs args;
    args.VertexCount = meta.VertexCount;
    args.InstanceCount = 0u;
    args.FirstVertex = 0u;
    args.FirstInstance = 0u;
    gpu_store<DrawIndirectArgs>(root.DrawArgs, id.x, args);
}
