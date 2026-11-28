using System.Runtime.InteropServices;

namespace Alco.World3D;

/// <summary>Configures the zero-CPU-BVH GPU-driven voxel global illumination clipmap and tracer.</summary>
public sealed class World3DVoxelGiSettings
{
    /// <summary>Gets or sets whether VoxelGI updates and cone-traced indirect illumination are active.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the base voxel resolution per axis for each clipmap cascade (power of two in [32, 256]).</summary>
    public int Resolution { get; set; } = 128;

    /// <summary>Gets or sets the finest cascade voxel size in meters.</summary>
    public float BaseVoxelSize { get; set; } = 0.25f;

    /// <summary>Gets or sets the persistent static sparse brick pool capacity in 8x8x8 bricks.</summary>
    public int StaticBrickPoolCapacity { get; set; } = 8192;

    /// <summary>Gets or sets the transient dynamic sparse brick pool capacity in 8x8x8 bricks.</summary>
    public int DynamicBrickPoolCapacity { get; set; } = 2048;

    /// <summary>Gets or sets how many finest clipmap cascades accept dynamic instances ([0, 4]).</summary>
    public int DynamicLevelCount { get; set; } = 2;

    /// <summary>Gets or sets the per-frame newly dirty static brick budget for cascades 0..3.</summary>
    public int[] StaticBrickBudgets { get; set; } = [1024, 512, 256, 256];

    /// <summary>Gets or sets whether cascade resets drain up to the full static pool in a single frame instead of obeying <see cref="StaticBrickBudgets"/>.</summary>
    public bool BootstrapFullVolumeOnReset { get; set; }

    /// <summary>Gets or sets the multiplier applied to emissive radiance during voxel injection.</summary>
    public float EmissiveBoost { get; set; } = 1.0f;

    /// <summary>Gets or sets the indirect diffuse GI contribution multiplier.</summary>
    public float IndirectDiffuseIntensity { get; set; } = 1.0f;

    /// <summary>Gets or sets the indirect specular cone-tracing contribution multiplier.</summary>
    public float IndirectSpecularIntensity { get; set; } = 1.0f;

    /// <summary>Gets or sets the multi-bounce directional propagation retention factor per sweep.</summary>
    public float PropagationDecay { get; set; } = 0.88f;

    /// <summary>Gets or sets the temporal convergence rate (1/s) used when demosaicing traced diffuse GI.</summary>
    public float TemporalBlendSpeed { get; set; } = 8.0f;

    /// <summary>Gets or sets whether GI cone tracing runs at checkerboard half resolution instead of full resolution.</summary>
    public bool HalfResolutionTrace { get; set; }

    /// <summary>Gets or sets the maximum number of dynamic point lights uploaded to VoxelGI and deferred lighting.</summary>
    public int MaxPointLights { get; set; } = 64;
}

/// <summary>Stores the GPU-driven brick pool, fallback, and indirect dispatch counters read back with low frequency.</summary>
[StructLayout(LayoutKind.Sequential, Size = 40)]
public readonly record struct World3DVoxelGiGpuCounters(
    uint StaticUsedBricks,
    uint StaticAllocatedThisFrame,
    uint StaticReclaimedThisFrame,
    uint StaticFallbackBricks,
    uint StaticDirtyQueued,
    uint StaticDirtyDispatched,
    uint DynamicUsedBricks,
    uint DynamicFallbackBricks,
    uint ChunkCandidateInstances,
    uint BrickDispatchChunks);

/// <summary>Summarizes the recorded VoxelGI state and latest async GPU readback counters for one frame.</summary>
public readonly record struct World3DVoxelGiStats(
    bool Enabled,
    int Resolution,
    int CascadeCount,
    float BaseVoxelSize,
    int StaticPoolCapacity,
    int DynamicPoolCapacity,
    int EvictedStaticBricksThisFrame,
    int ResetCascadeMask,
    int ShiftedCascadeMask,
    int PointLightCount,
    World3DVoxelGiGpuCounters LastReadbackCounters);
