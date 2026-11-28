using System.Runtime.InteropServices;

namespace Alco.Rendering;

/// <summary>Selects spatial metering weight distribution for <see cref="RGNode_AutoExposure"/>.</summary>
public enum AutoExposureMeteringMode : byte
{
    /// <summary>Weights all pixels across the screen equally.</summary>
    Uniform = 0,

    /// <summary>Weights pixels smoothly toward the viewport center.</summary>
    CenterWeighted = 1,

    /// <summary>Samples only inside the central metering circle.</summary>
    Spot = 2,
}

/// <summary>GPU-side persistent state produced by <see cref="RGNode_AutoExposure"/>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct AutoExposureState
{
    /// <summary>Final linear exposure multiplier applied before tone mapping.</summary>
    public float Exposure;

    /// <summary>Reciprocal of <see cref="Exposure"/>.</summary>
    public float InvExposure;

    /// <summary>Temporally adapted EV value clamped to <c>[MinEV, MaxEV]</c>.</summary>
    public float AdaptedEV;

    /// <summary>Current frame target EV computed from the clipped luminance histogram.</summary>
    public float TargetEV;

    /// <summary>Average scene luminance derived from the clipped histogram.</summary>
    public float AverageLuminance;

    /// <summary>Fixed-point total metering weight accumulated from valid pixels.</summary>
    public uint SampledWeight;

    /// <summary>Non-zero once the persistent GPU buffer holds initialized state.</summary>
    public uint Initialized;

    /// <summary>Reserved alignment padding.</summary>
    public uint Pad0;
}

/// <summary>Configuration parameters for histogram-based automatic exposure.</summary>
public record struct AutoExposureSettings
{
    /// <summary>Minimum allowed adapted EV.</summary>
    public float MinEV;

    /// <summary>Maximum allowed adapted EV.</summary>
    public float MaxEV;

    /// <summary>Manual exposure compensation in EV stops.</summary>
    public float ExposureCompensation;

    /// <summary>Target middle-gray key value used to convert scene luminance into EV.</summary>
    public float KeyValue;

    /// <summary>Lower cumulative histogram percentile excluded from average luminance (0..1).</summary>
    public float LowPercent;

    /// <summary>Upper cumulative histogram percentile excluded from average luminance (0..1).</summary>
    public float HighPercent;

    /// <summary>Minimum log2 luminance mapped onto the histogram range.</summary>
    public float MinLogLuminance;

    /// <summary>Maximum log2 luminance mapped onto the histogram range.</summary>
    public float MaxLogLuminance;

    /// <summary>Adaptation speed (1/s) when scene brightness increases.</summary>
    public float SpeedUp;

    /// <summary>Adaptation speed (1/s) when scene brightness decreases.</summary>
    public float SpeedDown;

    /// <summary>Spatial metering weighting mode.</summary>
    public AutoExposureMeteringMode MeteringMode;

    /// <summary>Normalized radius controlling center-weighted and spot falloff.</summary>
    public float CenterWeightRadius;

    /// <summary>When true, excludes underflow dark-floor bin 0 from average luminance.</summary>
    public bool IgnoreDarkFloor;

    /// <summary>When true, samples the input HDR texture at half resolution per axis.</summary>
    public bool HalfResolution;

    /// <summary>Default balanced auto-exposure settings.</summary>
    public static readonly AutoExposureSettings Default = new()
    {
        MinEV = -4.0f,
        MaxEV = 8.0f,
        ExposureCompensation = 0.0f,
        KeyValue = 0.18f,
        LowPercent = 0.40f,
        HighPercent = 0.95f,
        MinLogLuminance = -10.0f,
        MaxLogLuminance = 14.0f,
        SpeedUp = 3.0f,
        SpeedDown = 1.0f,
        MeteringMode = AutoExposureMeteringMode.CenterWeighted,
        CenterWeightRadius = 0.75f,
        IgnoreDarkFloor = true,
        HalfResolution = true,
    };
}
