#language slang 2025
module World3DBuildArgs;

import Core;
import World3DSceneData;

public struct DrawCandidate
{
    public uint InstanceSlotPlusOne;
    public uint PageId;
};

public struct DrawRef
{
    public uint InstanceSlot;
    public uint Lod;
};

public struct IndexedDrawArgs
{
    public uint IndexCount;
    public uint InstanceCount;
    public uint FirstIndex;
    public int VertexOffset;
    public uint FirstInstance;
};

[GPURoot]
public struct BuildArgsRoot
{
    public GPUBufferView Directory;
    public GPUBufferView Candidates;
    public GPUBufferView Selection;
    public GPUBufferView Args;
    public GPUBufferView DrawRefs;
    public GPUBufferView TargetViewSlots;
    public GPUBufferView Phase2Args;
    public uint InstanceCount;
    public uint CandidateCount;
    public uint GroupsX;
    public uint Flags; // bit 0 = DirectZeroBase (Phase 2 single view), bit 1 = ClearPhase2Args when viewSlot == 0
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void MainCS(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    BuildArgsRoot root = gpu_load_root<BuildArgsRoot>();
    uint i = (group.y * root.GroupsX + group.x) * 64u + lane;
    if (i >= root.CandidateCount)
        return;

    bool directZeroBase = (root.Flags & 1u) != 0u;
    uint viewSlot = directZeroBase ? 0u : gpu_load<uint>(root.TargetViewSlots, group.z);

    DrawCandidate candidate = gpu_load<DrawCandidate>(root.Candidates, i);
    IndexedDrawArgs args = (IndexedDrawArgs)0;
    DrawRef drawRef = (DrawRef)0;
    if (candidate.InstanceSlotPlusOne != 0u)
    {
        uint slot = candidate.InstanceSlotPlusOne - 1u;
        uint selected = gpu_load<uint>(root.Selection, viewSlot * root.InstanceCount + slot);
        if (selected < WORLD3D_MAX_GEOMETRY_LODS)
        {
            World3DInstanceGpu instance = world3d_load_instance(root.Directory, slot);
            World3DGeometryLodGpu lod = world3d_load_geometry_lod(root.Directory, instance.GeometrySlot, selected);
            if (candidate.PageId == lod.PageId && lod.IndexCount != 0u)
            {
                args.IndexCount = lod.IndexCount;
                args.InstanceCount = 1u;
                args.FirstIndex = lod.IndexByteOffset / 4u;
                drawRef.InstanceSlot = slot;
                drawRef.Lod = selected;
            }
        }
    }

    uint dst = viewSlot * root.CandidateCount + i;
    gpu_store<IndexedDrawArgs>(root.Args, dst, args);
    gpu_store<DrawRef>(root.DrawRefs, dst, drawRef);

    if (!directZeroBase && (root.Flags & 2u) != 0u && viewSlot == 0u)
        gpu_store<IndexedDrawArgs>(root.Phase2Args, i, (IndexedDrawArgs)0);
}
