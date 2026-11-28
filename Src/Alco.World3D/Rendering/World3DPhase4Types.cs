using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>Controls GPU-driven forward transparent and transmissive glass rendering.</summary>
public sealed class World3DForwardSettings
{
    /// <summary>Gets or sets whether forward transparent and glass rendering is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether forward draw selection culls occluded transparent instances against Phase 2 Hi-Z.</summary>
    public bool UseHiZOcclusion { get; set; } = true;

    /// <summary>Gets or sets whether transparent instances sort front-to-back instead of back-to-front.</summary>
    public bool SortFrontToBack { get; set; }

    /// <summary>Gets or sets whether transmissive materials sample the opaque scene snapshot for screen-space refraction.</summary>
    public bool EnableRefraction { get; set; } = true;

    /// <summary>Gets or sets the screen-space refraction offset scale multiplied by optical IOR strength.</summary>
    public float RefractionScale { get; set; } = 0.05f;

    /// <summary>Gets or sets the maximum number of transparent candidate slots sorted and emitted per frame.</summary>
    public int MaxTransparentInstances { get; set; } = 4096;
}

/// <summary>Reports recorded GPU-driven forward pass telemetry for the latest frame.</summary>
/// <param name="Enabled">True when the forward pass executed in the recorded frame.</param>
/// <param name="CandidateCount">Total resident transparent candidate slots staged for GPU culling.</param>
/// <param name="VisibleDraws">Latest async readback count of GPU-selected transparent draws, or zero before readback.</param>
/// <param name="BucketCount">Number of active PSO/geometry-page buckets in the forward layout.</param>
/// <param name="RefractionActive">True when the scene color snapshot was captured for glass refraction.</param>
public readonly record struct World3DForwardStats(
    bool Enabled,
    uint CandidateCount,
    uint VisibleDraws,
    int BucketCount,
    bool RefractionActive);

/// <summary>Controls GPU ray-marched volumetric clouds and same-frame top-down cloud shadows.</summary>
public sealed class World3DVolumetricCloudsSettings
{
    /// <summary>Gets or sets whether volumetric cloud rendering is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether clouds bake a camera-centered top-down sun shadow map for terrain, GI, and shafts.</summary>
    public bool CastShadows { get; set; } = true;

    /// <summary>Gets or sets the internal cloud raymarch resolution scale in [0.25, 1.0].</summary>
    public float ResolutionScale { get; set; } = 0.5f;

    /// <summary>Gets or sets the maximum primary raymarch steps inside the cloud layer.</summary>
    public int MaxSteps { get; set; } = 64;

    /// <summary>Gets or sets the sun light-march sample count per cloud step.</summary>
    public int LightSteps { get; set; } = 6;

    /// <summary>Gets or sets the bottom altitude of the cloud slab in meters.</summary>
    public float CloudBottomMeters { get; set; } = 1200f;

    /// <summary>Gets or sets the top altitude of the cloud slab in meters.</summary>
    public float CloudTopMeters { get; set; } = 2800f;

    /// <summary>Gets or sets cloud sky coverage fraction in [0, 1].</summary>
    public float Coverage { get; set; } = 0.52f;

    /// <summary>Gets or sets base cloud extinction density coefficient.</summary>
    public float Density { get; set; } = 0.035f;

    /// <summary>Gets or sets high-frequency noise erosion intensity in [0, 1].</summary>
    public float DetailStrength { get; set; } = 0.35f;

    /// <summary>Gets or sets wispy erosion modulation strength in [0, 1].</summary>
    public float ErosionStrength { get; set; } = 0.45f;

    /// <summary>Gets or sets cloud layer wind velocity in meters per second (+X/+Y/+Z).</summary>
    public Vector3 WindVelocity { get; set; } = new(18f, 6f, 0f);

    /// <summary>Gets or sets temporal reprojection history blend factor in [0, 0.98].</summary>
    public float TemporalBlend { get; set; } = 0.92f;

    /// <summary>Gets or sets cloud shadow attenuation strength in [0, 1].</summary>
    public float ShadowStrength { get; set; } = 0.72f;

    /// <summary>Gets or sets world-space side length covered by the camera-centered cloud shadow map in meters.</summary>
    public float ShadowCoverageMeters { get; set; } = 16384f;

    /// <summary>Gets or sets square resolution of the cloud shadow map.</summary>
    public int ShadowResolution { get; set; } = 256;

    /// <summary>Gets or sets overall cloud absorption/extinction multiplier.</summary>
    public float ExtinctionScale { get; set; } = 1.0f;

    /// <summary>Gets or sets dual-octave multiple scattering energy contribution.</summary>
    public float MultiScatterStrength { get; set; } = 0.45f;

    /// <summary>Gets or sets forward silver-lining highlight strength around the sun disc.</summary>
    public float SilverLiningIntensity { get; set; } = 0.35f;

    /// <summary>Gets or sets forward Henyey-Greenstein phase eccentricity.</summary>
    public float ForwardPhaseG { get; set; } = 0.65f;

    /// <summary>Gets or sets backward Henyey-Greenstein phase eccentricity.</summary>
    public float BackPhaseG { get; set; } = -0.25f;

    /// <summary>Gets or sets maximum trace distance along shallow horizon rays in meters.</summary>
    public float MaxDistanceMeters { get; set; } = 60000f;
}

/// <summary>Reports recorded volumetric cloud telemetry for the latest frame.</summary>
/// <param name="Enabled">True when volumetric clouds rendered in the recorded frame.</param>
/// <param name="CastShadows">True when the same-frame cloud shadow map was baked.</param>
/// <param name="TraceExtent">Dimensions of the cloud raymarch trace target.</param>
/// <param name="ShadowExtent">Dimensions of the baked cloud shadow map.</param>
/// <param name="TemporalHistoryValid">True when the temporal cloud history buffer was valid and reprojected.</param>
/// <param name="FrameIndex">Monotonic cloud frame counter driving Bayer subpixel offsets.</param>
public readonly record struct World3DVolumetricCloudsStats(
    bool Enabled,
    bool CastShadows,
    Extent2D TraceExtent,
    Extent2D ShadowExtent,
    bool TemporalHistoryValid,
    uint FrameIndex);

/// <summary>Controls shadowed atmospheric volumetric light scattering (god rays and exponential height fog).</summary>
public sealed class World3DVolumetricLightSettings
{
    /// <summary>Gets or sets whether volumetric light scattering is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether volumetric raymarching executes at half screen resolution before bilateral upsampling.</summary>
    public bool HalfResolution { get; set; } = true;

    /// <summary>Gets or sets raymarch step count per pixel.</summary>
    public int StepCount { get; set; } = 24;

    /// <summary>Gets or sets maximum scattering march distance in meters.</summary>
    public float MaxDistanceMeters { get; set; } = 160f;

    /// <summary>Gets or sets direct sun volumetric shaft intensity multiplier.</summary>
    public float Intensity { get; set; } = 0.45f;

    /// <summary>Gets or sets Henyey-Greenstein phase anisotropy in [0, 0.99].</summary>
    public float Anisotropy { get; set; } = 0.65f;

    /// <summary>Gets or sets exponential altitude fog decay rate per meter.</summary>
    public float HeightFalloff { get; set; } = 0.025f;

    /// <summary>Gets or sets reference world altitude in meters where fog density peaks.</summary>
    public float BaseHeightMeters { get; set; }

    /// <summary>Gets or sets baseline volumetric medium density.</summary>
    public float BaseDensity { get; set; } = 0.008f;

    /// <summary>Gets or sets sky-tinted ambient in-scattering intensity in shadowed regions.</summary>
    public float AmbientFogIntensity { get; set; } = 0.025f;
}

/// <summary>Reports recorded volumetric light telemetry for the latest frame.</summary>
/// <param name="Enabled">True when volumetric light rendered in the recorded frame.</param>
/// <param name="TraceExtent">Dimensions of the scattering march buffer.</param>
/// <param name="StepCount">Effective raymarch steps per pixel.</param>
public readonly record struct World3DVolumetricLightStats(
    bool Enabled,
    Extent2D TraceExtent,
    int StepCount);

/// <summary>Controls Hi-Z accelerated stochastic screen-space glossy reflections.</summary>
public sealed class World3DSsrSettings
{
    /// <summary>Gets or sets whether screen-space reflections are enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether Hi-Z ray tracing and spatial/temporal resolve run at half screen resolution.</summary>
    public bool HalfResolution { get; set; } = true;

    /// <summary>Gets or sets whether DeferredLighting suppresses VoxelGI glossy specular completely when SSR is active.</summary>
    public bool DisableVoxelSpecularFallback { get; set; }

    /// <summary>Gets or sets maximum hierarchical Hi-Z trace steps per ray.</summary>
    public int MaxSteps { get; set; } = 48;

    /// <summary>Gets or sets world-space screen depth thickness tolerance in meters for hit acceptance.</summary>
    public float ThicknessMeters { get; set; } = 0.5f;

    /// <summary>Gets or sets maximum trace distance in meters.</summary>
    public float MaxDistanceMeters { get; set; } = 60f;

    /// <summary>Gets or sets roughness threshold above which surfaces smoothly skip SSR and keep VoxelGI specular.</summary>
    public float MaxRoughness { get; set; } = 0.6f;

    /// <summary>Gets or sets intensity multiplier applied to resolved SSR radiance.</summary>
    public float Intensity { get; set; } = 1.0f;

    /// <summary>Gets or sets roughness-weighted temporal reprojection history blend factor in [0, 0.96].</summary>
    public float TemporalBlend { get; set; } = 0.9f;

    /// <summary>Gets or sets normal-guided screen edge fade fraction in [0.01, 0.5].</summary>
    public float EdgeFade { get; set; } = 0.15f;

    /// <summary>Gets or sets whether ray tracing rejects backfacing screen hits using G-Buffer normals.</summary>
    public bool RejectBackfaceHits { get; set; } = true;
}

/// <summary>Reports recorded Hi-Z SSR telemetry for the latest frame.</summary>
/// <param name="Enabled">True when SSR executed in the recorded frame.</param>
/// <param name="TraceExtent">Dimensions of the trace and resolve buffers.</param>
/// <param name="MaxSteps">Effective maximum hierarchical Hi-Z steps per ray.</param>
/// <param name="TemporalHistoryValid">True when the temporal SSR history buffer was valid and reprojected.</param>
/// <param name="HiZActive">True when Phase 2 Hi-Z pyramid acceleration was active for tracing.</param>
public readonly record struct World3DSsrStats(
    bool Enabled,
    Extent2D TraceExtent,
    int MaxSteps,
    bool TemporalHistoryValid,
    bool HiZActive);

/// <summary>Controls histogram-based automatic exposure and exposure-aware bloom thresholding for a <see cref="World3DScene"/>.</summary>
public sealed class World3DAutoExposureSettings
{
    /// <summary>Gets or sets whether automatic exposure is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether bloom setup scales its threshold using the adapted scene exposure.</summary>
    public bool ExposureAwareBloom { get; set; } = true;

    /// <summary>Gets or sets the minimum adapted EV stop.</summary>
    public float MinEV { get; set; } = -3.0f;

    /// <summary>Gets or sets the maximum adapted EV stop.</summary>
    public float MaxEV { get; set; } = 6.0f;

    /// <summary>Gets or sets manual exposure compensation in EV stops.</summary>
    public float ExposureCompensation { get; set; }

    /// <summary>Gets or sets the middle-gray key value converting average scene luminance to EV.</summary>
    public float KeyValue { get; set; } = 0.18f;

    /// <summary>Gets or sets the lower histogram percentile excluded from metering (0..1).</summary>
    public float LowPercent { get; set; } = 0.40f;

    /// <summary>Gets or sets the upper histogram percentile excluded from metering (0..1).</summary>
    public float HighPercent { get; set; } = 0.95f;

    /// <summary>Gets or sets the minimum log2 luminance spanned by the histogram.</summary>
    public float MinLogLuminance { get; set; } = -10.0f;

    /// <summary>Gets or sets the maximum log2 luminance spanned by the histogram.</summary>
    public float MaxLogLuminance { get; set; } = 14.0f;

    /// <summary>Gets or sets adaptation speed (1/s) when adapting to brighter scenes.</summary>
    public float SpeedUp { get; set; } = 3.0f;

    /// <summary>Gets or sets adaptation speed (1/s) when adapting to darker scenes.</summary>
    public float SpeedDown { get; set; } = 1.0f;

    /// <summary>Gets or sets the spatial metering weight mode.</summary>
    public AutoExposureMeteringMode MeteringMode { get; set; } = AutoExposureMeteringMode.CenterWeighted;

    /// <summary>Gets or sets the normalized center falloff radius for weighted/spot metering.</summary>
    public float CenterWeightRadius { get; set; } = 0.75f;

    /// <summary>Gets or sets whether underflow dark-floor pixels (bin 0) are excluded from metering.</summary>
    public bool IgnoreDarkFloor { get; set; } = true;

    /// <summary>Gets or sets whether luminance histogram sampling runs at half viewport resolution.</summary>
    public bool HalfResolution { get; set; } = true;

    /// <summary>Gets or sets whether non-blocking async telemetry readback is scheduled for diagnostics.</summary>
    public bool TelemetryEnabled { get; set; }
}

/// <summary>Reports automatic exposure status and latest telemetry for a <see cref="World3DScene"/> frame.</summary>
/// <param name="Enabled">True when automatic exposure ran in the recorded frame.</param>
/// <param name="HasTelemetry">True when at least one completed GPU telemetry snapshot is populated.</param>
/// <param name="Exposure">Latest adapted linear exposure multiplier.</param>
/// <param name="AdaptedEV">Latest adapted EV stop.</param>
/// <param name="TargetEV">Latest target EV stop computed from the luminance histogram.</param>
/// <param name="AverageLuminance">Latest clipped average scene luminance.</param>
public readonly record struct World3DAutoExposureStats(
    bool Enabled,
    bool HasTelemetry,
    float Exposure,
    float AdaptedEV,
    float TargetEV,
    float AverageLuminance);
