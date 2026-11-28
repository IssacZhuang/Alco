using System;
using Alco.Graphics;

namespace Alco.World3D;

/// <summary>Configures GPU two-phase Hi-Z occlusion culling for the primary view.</summary>
public sealed class World3DOcclusionSettings
{
    /// <summary>Gets or sets whether Hi-Z occlusion testing is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether previously occluded candidates are retested after Hi-Z Build 1 and redrawn via DispatchIndirect.</summary>
    public bool TwoPhase { get; set; } = true;
}

/// <summary>Configures 1km+ far horizon heightfield shadows and the optional static far cascade.</summary>
public sealed class World3DFarShadowSettings
{
    /// <summary>Gets or sets whether far horizon heightfield shadowing is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether static shadow casters near the origin anchor are automatically captured into the far heightfield.</summary>
    public bool AutoCaptureFromScene { get; set; } = true;

    /// <summary>Gets or sets the world-space coverage width and depth in meters of the far heightfield centered on the origin anchor cell.</summary>
    public float WorldCoverageMeters { get; set; } = 16384f;

    /// <summary>Gets or sets the square resolution in texels of the generated far heightfield Max-Mipmap pyramid.</summary>
    public int Resolution { get; set; } = 512;

    /// <summary>Gets or sets the maximum horizon ray-march distance in meters for far terrain/city shadows.</summary>
    public float HorizonMaxDistance { get; set; } = 4096f;

    /// <summary>Gets or sets whether the optional fifth static far cascade slice is enabled.</summary>
    public bool EnableStaticFarCascade { get; set; } = true;

    /// <summary>Gets or sets the outer world-space radius in meters covered by the static far cascade.</summary>
    public float StaticFarDistance { get; set; } = 1024f;

    /// <summary>Gets or sets the minimum bounding-sphere radius in meters for objects drawn into the static far cascade.</summary>
    public float StaticFarMinObjectRadius { get; set; } = 4f;

    /// <summary>Gets or sets the minimum interval in frames between periodic refreshes of the static far cascade.</summary>
    public int StaticFarRefreshIntervalFrames { get; set; } = 60;
}

/// <summary>Configures the Reflective Shadow Map pass feeding Phase 3 VoxelGI injection and sun bounce.</summary>
public sealed class World3DRsmSettings
{
    /// <summary>Gets or sets whether the RSM pass executes.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the square texture resolution in texels for RSM flux, normal, and depth targets.</summary>
    public int Resolution { get; set; } = 512;

    /// <summary>Gets or sets the world-space orthographic extent in meters centered on the active camera.</summary>
    public float WorldExtentMeters { get; set; } = 96f;
}

/// <summary>Configures tiered-cached cascaded shadow maps, Min-Max acceleration, far shadows, and RSM.</summary>
public sealed class World3DShadowSettings
{
    /// <summary>Gets or sets whether directional sun shadows are enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the per-cascade square shadow map resolution in texels.</summary>
    public int CascadeResolution { get; set; } = 2048;

    /// <summary>Gets or sets the maximum shadow coverage distance in meters across the four main CSM cascades.</summary>
    public float MaxDistance { get; set; } = 220f;

    /// <summary>Gets or sets the practical CSM split factor in [0, 1] blending uniform and logarithmic splits.</summary>
    public float CascadeSplitLambda { get; set; } = 0.85f;

    /// <summary>Gets or sets the shadow tightness factor in [0, 1] trading penumbra softness for sharper PCF filtering.</summary>
    public float ShadowTightness { get; set; } = 0.72f;

    /// <summary>Gets or sets whether 1/8-resolution Min-Max shadow depth pyramids accelerate PCF early-out.</summary>
    public bool UseMinMaxAcceleration { get; set; } = true;

    /// <summary>Gets or sets the far-cascade shadow caster LOD bias scale in meters per texel.</summary>
    public float MinTexelSizeLodBiasMeters { get; set; } = 0.04f;

    /// <summary>Gets or sets per-cascade minimum bounding-sphere radii in meters for dynamic small-object filtering across cascades 0..3.</summary>
    public float[] CascadeMinObjectRadius { get; set; } = [0f, 0f, 0.35f, 1.0f];

    /// <summary>Gets or sets the refresh intervals in frames across cascades 0..3 (tier 0 = every frame).</summary>
    public int[] CascadeUpdateIntervals { get; set; } = [1, 1, 2, 4];

    /// <summary>Gets the 1km+ far horizon heightfield and optional static far cascade configuration.</summary>
    public World3DFarShadowSettings FarField { get; } = new();

    /// <summary>Gets the optional RSM pass configuration.</summary>
    public World3DRsmSettings Rsm { get; } = new();
}

/// <summary>Selects a diagnostic visualization overlay produced by the deferred lighting pass.</summary>
public enum World3DLightingDebugView
{
    /// <summary>Standard physically-based deferred lighting output.</summary>
    None = 0,
    /// <summary>Color-codes CSM cascades 0..3 and layer 4 static far / horizon falloff.</summary>
    CascadeIndex = 1,
    /// <summary>Outputs the scalar resolved sun visibility factor in [0, 1].</summary>
    ShadowFactor = 2,
    /// <summary>Highlights Min-Max shadow pyramid full-lit, full-shadow, and mixed PCF regions.</summary>
    ShadowMinMaxEarlyOut = 3,
    /// <summary>Displays blending weights between CSM, optional static far cascade, and far heightfield horizon shadows.</summary>
    FarShadowBlend = 4,
    /// <summary>Visualizes Hi-Z mip 0 farthest (R) and nearest (G) Reversed-Z depths.</summary>
    HiZMip0 = 5,
    /// <summary>Visualizes the optional RSM radiant flux target when RSM is enabled.</summary>
    RsmFlux = 6,
    /// <summary>Visualizes resolved VoxelGI indirect diffuse irradiance.</summary>
    GiDiffuse = 7,
    /// <summary>Visualizes resolved VoxelGI indirect glossy specular radiance.</summary>
    GiSpecular = 8,
    /// <summary>Visualizes resolved VoxelGI directional ambient visibility.</summary>
    GiVisibility = 9,
    /// <summary>Visualizes raw noisy half-res VoxelGI diffuse trace output prior to spatial demosaic.</summary>
    GiRawDiffuseTrace = 10,
    /// <summary>Visualizes volumetric cloud column opacity and cloud shadow density.</summary>
    CloudOpacity = 11,
    /// <summary>Visualizes screen-space glossy reflection radiance prior to composite.</summary>
    SsrReflection = 12,
    /// <summary>Visualizes screen-space reflection confidence and hit mask.</summary>
    SsrConfidence = 13,
}

/// <summary>Summarizes the recorded occlusion state and latest async GPU culling readback counts.</summary>
/// <param name="Enabled">True when Hi-Z occlusion testing was enabled for the recorded frame.</param>
/// <param name="TwoPhase">True when Phase 2 retest and DispatchIndirect redraw were active.</param>
/// <param name="HiZExtent">Extent in texels of Hi-Z mip 0.</param>
/// <param name="HiZMipCount">Total mip levels in the Hi-Z pyramid.</param>
/// <param name="HasReadbackCounts">True when at least one completed frame's GPU counters have been read back.</param>
/// <param name="Phase1VisibleDraws">Phase 1 surviving draw count from the latest completed GPU readback.</param>
/// <param name="OccludedCandidates">Number of previously occluded candidates queued for Phase 2 retest.</param>
/// <param name="Phase2VisibleDraws">Phase 2 resurrected draw count from the latest completed GPU readback.</param>
public readonly record struct World3DOcclusionStats(
    bool Enabled,
    bool TwoPhase,
    Extent2D HiZExtent,
    uint HiZMipCount,
    bool HasReadbackCounts,
    uint Phase1VisibleDraws,
    uint OccludedCandidates,
    uint Phase2VisibleDraws);

/// <summary>Summarizes shadow map updates, caching, and far-field status for the last recorded frame.</summary>
/// <param name="Enabled">True when directional sun shadows were enabled.</param>
/// <param name="CascadeResolution">Resolution in texels of each main/static cascade slice.</param>
/// <param name="UpdatedCascadeMask">Bitmask of CSM/StaticFar slices re-rendered during the frame (bits 0..4).</param>
/// <param name="CachedCascadeMask">Bitmask of CSM/StaticFar slices reused via cached world-space projection (bits 0..4).</param>
/// <param name="ShadowDrawCalls">Total multi-draw indirect shadow batches recorded across updated cascades.</param>
/// <param name="MinMaxActive">True when Min-Max shadow depth acceleration ran for the frame.</param>
/// <param name="FarHeightfieldActive">True when the far heightfield horizon map is active for sampling.</param>
/// <param name="StaticFarCascadeActive">True when slice 4 static far cascade is active.</param>
/// <param name="RsmUpdated">True when the optional RSM pass rendered during the frame.</param>
public readonly record struct World3DShadowStats(
    bool Enabled,
    int CascadeResolution,
    uint UpdatedCascadeMask,
    uint CachedCascadeMask,
    uint ShadowDrawCalls,
    bool MinMaxActive,
    bool FarHeightfieldActive,
    bool StaticFarCascadeActive,
    bool RsmUpdated);
