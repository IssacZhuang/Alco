#language slang 2025
module World3DOcclusionRetest;

import Core;
import World3DSceneData;
import World3DOcclusionCommon;

[GPURoot]
public struct OcclusionRetestRoot
{
    public GPUBufferView Directory;
    public GPUBufferView Views;
    public GPUBufferView Counters;
    public GPUBufferView OccludedCandidates;
    public GPUBufferView Phase2Selection;
    public GPUTextureId HiZTexture;
    public uint HiZMip0Width;
    public uint HiZMip0Height;
    public uint HiZMipCount;
    public uint Reserved0;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void MainCS(uint3 id : SV_DispatchThreadID)
{
    OcclusionRetestRoot root = gpu_load_root<OcclusionRetestRoot>();
    uint count = gpu_load<uint>(root.Counters, 0u);
    if (id.x >= count)
        return;

    World3DOccludedCandidate cand = gpu_load<World3DOccludedCandidate>(root.OccludedCandidates, id.x);
    World3DInstanceGpu instance = world3d_load_instance(root.Directory, cand.InstanceSlot);
    if ((instance.Flags & WORLD3D_INSTANCE_LIVE) == 0u)
        return;

    World3DView view = gpu_load<World3DView>(root.Views, 0u);
    uint2 viewport = uint2(view.ViewportSizeInvSize.xy);
    bool occluded = world3d_hiz_is_occluded(
        root.HiZTexture,
        viewport,
        uint2(root.HiZMip0Width, root.HiZMip0Height),
        root.HiZMipCount,
        instance.Spatial,
        view.EyeCell,
        view.EyeLocalNear.xyz,
        view.CameraVP,
        false);
    if (occluded)
        return;

    gpu_store<uint>(root.Phase2Selection, cand.InstanceSlot, cand.Lod);
    gpu_atomic_add_u32(gpu_offset_bytes(root.Counters, 8u), 1u);
}
