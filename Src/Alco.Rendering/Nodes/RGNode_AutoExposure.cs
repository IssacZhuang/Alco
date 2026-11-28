using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.Rendering;

/// <summary>
/// Multi-pass bindless <see cref="IRenderGraphNode"/> computing 256-bin log2 luminance histograms
/// and temporally adapted scene exposure into a persistent <see cref="RGBuffer"/> without modifying the input HDR texture.
/// </summary>
public sealed unsafe class RGNode_AutoExposure : IRenderGraphNode, IDisposable
{
    /// <summary>Number of histogram bins spanning <c>[MinLogLuminance, MaxLogLuminance]</c>.</summary>
    public const int BinCount = 256;

    private const uint ClearGroupSize = 256u;
    private const uint HistogramGroupDim = 16u;
    private const uint FlagForceReset = 1u << 0;
    private const uint FlagIgnoreDarkFloor = 1u << 1;
    private const ulong HistogramBufferBytes = BinCount * sizeof(uint);

    private const int PassIdClear = 0;
    private const int PassIdHistogram = 1;
    private const int PassIdAdapt = 2;
    private const int PassIdReadback = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct AutoExposureClearRoot
    {
        public GPUBufferView Histogram;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AutoExposureHistogramRoot
    {
        public uint Source;
        public uint SampleSizeX;
        public uint SampleSizeY;
        public uint MeteringMode;
        public Vector2 InvSampleSize;
        public float MinLogLuminance;
        public float InvLogLuminanceRange;
        public GPUBufferView Histogram;
        public float CenterWeightRadius;
        public uint Pad0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AutoExposureAdaptRoot
    {
        public GPUBufferView Histogram;
        public GPUBufferView State;
        public float MinLogLuminance;
        public float LogLuminanceRange;
        public float LowPercent;
        public float HighPercent;
        public float MinEV;
        public float MaxEV;
        public float ExposureCompensation;
        public float KeyValue;
        public float SpeedUp;
        public float SpeedDown;
        public float DeltaTime;
        public uint Flags;
    }

    private struct ReadbackSlot
    {
        public GpuBuffer? Buffer;
        public RenderContext? Context;
        public ulong FrameId;
        public ulong DiscardVersion;
        public bool Pending;
    }

    private readonly RenderingSystem _rendering;
    private readonly RenderChain? _chain;
    private readonly Func<RenderGraphBuilder, RGTexture>? _inputProvider;
    private readonly Shader _clearShader;
    private readonly Shader _histogramShader;
    private readonly Shader _adaptShader;
    private readonly GpuBuffer _stateBuffer;
    private readonly ReadbackSlot[] _readbackSlots = new ReadbackSlot[2];
    private readonly uint[] _lastHistogram = new uint[BinCount];

    private readonly string _clearPassName;
    private readonly string _histogramPassName;
    private readonly string _adaptPassName;
    private readonly string _readbackPassName;
    private readonly string _histogramBufferName;
    private readonly string _stateBufferName;
    private readonly string[] _readbackSlotNames;

    private bool _isEnabled = true;
    private AutoExposureSettings _settings = AutoExposureSettings.Default;
    private AutoExposureState _lastState;
    private bool _hasTelemetry;
    private bool _hasEverExecuted;
    private bool _disposed;

    private bool _hasSubmittedValidState;
    private RenderContext? _recordedResetContext;
    private ulong _recordedResetFrameId;
    private ulong _recordedResetDiscardVersion;

    private RGTexture _frameInput;
    private RGBuffer _frameHistogramBuffer;
    private RGBuffer _frameStateBuffer;
    private Extent3D _frameSampleExtent;
    private int _frameReadbackSlotIndex = -1;
    private RGBuffer _frameReadbackBuffer;

    /// <summary>Creates an auto-exposure node sampling the current texture on a <see cref="RenderChain"/>.</summary>
    /// <param name="rendering">The rendering system owning descriptor heaps and default shaders.</param>
    /// <param name="chain">The render chain whose <see cref="RenderChain.Current"/> HDR texture is metered.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    /// <param name="name">Diagnostic node name prefix used for pass and buffer labels.</param>
    public RGNode_AutoExposure(
        RenderingSystem rendering,
        RenderChain chain,
        ShaderSystem? shaders = null,
        string name = "AutoExposure")
        : this(rendering, chain ?? throw new ArgumentNullException(nameof(chain)), null, shaders, name)
    {
    }

    /// <summary>Creates an auto-exposure node sampling the HDR texture supplied by <paramref name="inputProvider"/>.</summary>
    /// <param name="rendering">The rendering system owning descriptor heaps and default shaders.</param>
    /// <param name="inputProvider">Callback resolving the source HDR texture during <see cref="Setup"/>.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    /// <param name="name">Diagnostic node name prefix used for pass and buffer labels.</param>
    public RGNode_AutoExposure(
        RenderingSystem rendering,
        Func<RenderGraphBuilder, RGTexture> inputProvider,
        ShaderSystem? shaders = null,
        string name = "AutoExposure")
        : this(rendering, null, inputProvider ?? throw new ArgumentNullException(nameof(inputProvider)), shaders, name)
    {
    }

    private RGNode_AutoExposure(
        RenderingSystem rendering,
        RenderChain? chain,
        Func<RenderGraphBuilder, RGTexture>? inputProvider,
        ShaderSystem? shaders,
        string name)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentException.ThrowIfNullOrEmpty(name);

        _rendering = rendering;
        _chain = chain;
        _inputProvider = inputProvider;
        Name = name;

        _clearPassName = $"{name}_Clear";
        _histogramPassName = $"{name}_Histogram";
        _adaptPassName = $"{name}_Adapt";
        _readbackPassName = $"{name}_Readback";
        _histogramBufferName = $"{name}_HistogramBuf";
        _stateBufferName = $"{name}_State";
        _readbackSlotNames = [$"{name}_Readback0", $"{name}_Readback1"];

        ShaderSystem shaderSystem = shaders ?? rendering.Shaders;
        shaderSystem.Compiler.RegisterVirtualModule(
            "AlcoRendering_AutoExposure",
            "AlcoRendering_AutoExposure.slang",
            LoadEmbeddedShaderSource("AlcoRendering_AutoExposure.slang"));

        _clearShader = LoadComputeShader(shaderSystem, "AutoExposureClear");
        _histogramShader = LoadComputeShader(shaderSystem, "AutoExposureHistogram");
        _adaptShader = LoadComputeShader(shaderSystem, "AutoExposureAdapt");

        VerifyRootLayout<AutoExposureClearRoot>(_clearShader, "AutoExposureClearRoot");
        VerifyRootLayout<AutoExposureHistogramRoot>(_histogramShader, "AutoExposureHistogramRoot");
        VerifyRootLayout<AutoExposureAdaptRoot>(_adaptShader, "AutoExposureAdaptRoot");

        _stateBuffer = rendering.CreateBuffer((ulong)sizeof(AutoExposureState), MemoryType.GpuOnly);
    }

    /// <summary>The diagnostic node name prefix.</summary>
    public string Name { get; }

    /// <summary>Whether this node participates in the frame graph.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (!value)
            {
                OutputBuffer = default;
            }
        }
    }

    /// <summary>When true, skips histogram computation and clears <see cref="OutputBuffer"/> for this frame.</summary>
    public bool Bypass { get; set; }

    /// <summary>Auto-exposure histogram and temporal adaptation parameters.</summary>
    public AutoExposureSettings Settings
    {
        get => _settings;
        set => _settings = value;
    }

    /// <summary>When true, marks the adapt pass with <see cref="RenderGraphPassBuilder.ProducesOutput"/> so headless graphs retain it.</summary>
    public bool AlwaysProducesOutput { get; set; }

    /// <summary>When true, schedules a non-blocking async readback of exposure state and histogram bins for diagnostics.</summary>
    public bool TelemetryEnabled { get; set; }

    /// <summary>The persistent <see cref="AutoExposureState"/> GPU buffer handle for the active frame, or default when disabled or bypassed.</summary>
    public RGBuffer OutputBuffer { get; private set; }

    /// <summary>Whether at least one completed async telemetry snapshot has been received.</summary>
    public bool HasTelemetry => _hasTelemetry;

    /// <summary>The most recently received async telemetry state snapshot.</summary>
    public AutoExposureState LastState => _lastState;

    /// <summary>The most recently received 256-bin luminance histogram weights.</summary>
    public ReadOnlySpan<uint> LastHistogram => _hasTelemetry ? _lastHistogram : ReadOnlySpan<uint>.Empty;

    /// <summary>Whether <see cref="Dispose"/> has been called.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Forces the next executed frame to snap immediately to the metered target exposure without temporal lag.</summary>
    public void Reset()
    {
        _hasSubmittedValidState = false;
        _recordedResetContext = null;
        _recordedResetFrameId = 0;
        _recordedResetDiscardVersion = 0;
    }

    /// <summary>Synchronously reads the latest GPU <see cref="AutoExposureState"/> into <paramref name="state"/>.</summary>
    /// <param name="state">Receives the read state on success.</param>
    /// <returns><see langword="true"/> when a GPU state snapshot was read back and initialized.</returns>
    public bool TryReadState(out AutoExposureState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        state = default;
        if (!_hasEverExecuted || _rendering.ActiveBackend == GraphicsBackend.NoGPU)
        {
            return false;
        }

        _rendering.ReadBuffer(_stateBuffer, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref state, 1)));
        return state.Initialized != 0u;
    }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        OutputBuffer = default;
        _frameInput = default;
        _frameHistogramBuffer = default;
        _frameStateBuffer = default;
        _frameSampleExtent = default;
        _frameReadbackSlotIndex = -1;
        _frameReadbackBuffer = default;

        if (_disposed || Bypass)
        {
            return;
        }

        RGTexture input = _chain != null ? _chain.Current : _inputProvider!(builder);
        if (!input.IsValid)
        {
            return;
        }

        Extent3D inputExtent = builder.GetExtent(input);
        if (inputExtent.X == 0 || inputExtent.Y == 0)
        {
            return;
        }

        Extent3D sampleExtent = _settings.HalfResolution
            ? new Extent3D(Math.Max(inputExtent.X >> 1, 1u), Math.Max(inputExtent.Y >> 1, 1u), 1u)
            : new Extent3D(inputExtent.X, inputExtent.Y, 1u);

        RGBuffer histogramBuffer = builder.CreateBuffer(new RGBufferDesc
        {
            Name = _histogramBufferName,
            SizeBytes = HistogramBufferBytes,
        });
        RGBuffer stateBuffer = builder.ImportBuffer(_stateBuffer, _stateBufferName);

        _frameInput = input;
        _frameHistogramBuffer = histogramBuffer;
        _frameStateBuffer = stateBuffer;
        _frameSampleExtent = sampleExtent;
        OutputBuffer = stateBuffer;

        RenderGraphPassBuilder clearPass = builder.AddPass(_clearPassName, PassIdClear);
        clearPass.Write(histogramBuffer, RGUsage.ComputeWrite);

        RenderGraphPassBuilder histogramPass = builder.AddPass(_histogramPassName, PassIdHistogram);
        histogramPass.Read(input, RGUsage.ComputeRead);
        histogramPass.ReadWrite(histogramBuffer, RGUsage.ComputeReadWrite);

        RenderGraphPassBuilder adaptPass = builder.AddPass(_adaptPassName, PassIdAdapt);
        adaptPass.Read(histogramBuffer, RGUsage.ComputeRead);
        adaptPass.ReadWrite(stateBuffer, RGUsage.ComputeReadWrite);
        if (AlwaysProducesOutput)
        {
            adaptPass.ProducesOutput();
        }

        if (TelemetryEnabled)
        {
            PollTelemetry();
            int freeSlot = AcquireFreeReadbackSlot();
            if (freeSlot >= 0)
            {
                RGBuffer readbackHandle = builder.ImportBuffer(
                    _readbackSlots[freeSlot].Buffer!,
                    _readbackSlotNames[freeSlot],
                    RGUsage.HostRead);
                _frameReadbackSlotIndex = freeSlot;
                _frameReadbackBuffer = readbackHandle;

                RenderGraphPassBuilder readbackPass = builder.AddPass(_readbackPassName, PassIdReadback);
                readbackPass.Read(stateBuffer, RGUsage.TransferRead);
                readbackPass.Read(histogramBuffer, RGUsage.TransferRead);
                readbackPass.Write(readbackHandle, RGUsage.TransferWrite);
                readbackPass.ProducesOutput();
            }
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context, int passId)
    {
        switch (passId)
        {
            case PassIdClear:
                ExecuteClear(in context);
                break;
            case PassIdHistogram:
                ExecuteHistogram(in context);
                break;
            case PassIdAdapt:
                ExecuteAdapt(in context);
                break;
            case PassIdReadback:
                ExecuteReadback(in context);
                break;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        OutputBuffer = default;
        if (!_stateBuffer.IsDisposed)
        {
            _stateBuffer.Dispose();
        }

        for (int i = 0; i < _readbackSlots.Length; ++i)
        {
            GpuBuffer? buffer = _readbackSlots[i].Buffer;
            if (buffer != null && !buffer.IsDisposed)
            {
                buffer.Dispose();
            }
            _readbackSlots[i] = default;
        }
    }

    private void ExecuteClear(in RenderGraphContext context)
    {
        GpuBuffer histogram = context.GetBuffer(_frameHistogramBuffer);
        AutoExposureClearRoot root = new()
        {
            Histogram = histogram.View,
        };

        using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
        compute.Dispatch(_clearShader, in root, new Extent3D(1u, 1u, 1u));
    }

    private void ExecuteHistogram(in RenderGraphContext context)
    {
        if (_frameSampleExtent.X == 0 || _frameSampleExtent.Y == 0)
        {
            return;
        }

        GetNormalizedLogLuminanceRange(out float minLogLum, out float logLumRange);
        Texture source = context.GetTexture(_frameInput);
        GpuBuffer histogram = context.GetBuffer(_frameHistogramBuffer);
        AutoExposureHistogramRoot root = new()
        {
            Source = source.SampledIndex,
            SampleSizeX = _frameSampleExtent.X,
            SampleSizeY = _frameSampleExtent.Y,
            MeteringMode = (uint)_settings.MeteringMode,
            InvSampleSize = new Vector2(1.0f / _frameSampleExtent.X, 1.0f / _frameSampleExtent.Y),
            MinLogLuminance = minLogLum,
            InvLogLuminanceRange = 1.0f / logLumRange,
            Histogram = histogram.View,
            CenterWeightRadius = Math.Clamp(
                float.IsFinite(_settings.CenterWeightRadius) ? _settings.CenterWeightRadius : 0.75f,
                0.05f,
                2.0f),
        };

        Extent3D groups = new(
            (_frameSampleExtent.X + HistogramGroupDim - 1u) / HistogramGroupDim,
            (_frameSampleExtent.Y + HistogramGroupDim - 1u) / HistogramGroupDim,
            1u);
        using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
        compute.Dispatch(_histogramShader, in root, groups);
    }

    private void ExecuteAdapt(in RenderGraphContext context)
    {
        RenderContext rc = context.RenderContext;
        if (!_hasSubmittedValidState &&
            _recordedResetContext != null &&
            !_recordedResetContext.IsDisposed &&
            _recordedResetContext.DiscardVersion == _recordedResetDiscardVersion &&
            _recordedResetContext.CurrentFrameId != _recordedResetFrameId)
        {
            _hasSubmittedValidState = true;
            _recordedResetContext = null;
            _recordedResetFrameId = 0;
        }

        bool forceReset = !_hasSubmittedValidState;
        if (forceReset)
        {
            _recordedResetContext = rc;
            _recordedResetFrameId = rc.CurrentFrameId;
            _recordedResetDiscardVersion = rc.DiscardVersion;
        }

        GetNormalizedLogLuminanceRange(out float minLogLum, out float logLumRange);
        float minEV = float.IsFinite(_settings.MinEV) ? _settings.MinEV : -4.0f;
        float maxEV = float.IsFinite(_settings.MaxEV) ? _settings.MaxEV : 8.0f;
        if (minEV > maxEV)
        {
            (minEV, maxEV) = (maxEV, minEV);
        }

        float lowPercent = Math.Clamp(float.IsFinite(_settings.LowPercent) ? _settings.LowPercent : 0.40f, 0.0f, 1.0f);
        float highPercent = Math.Clamp(float.IsFinite(_settings.HighPercent) ? _settings.HighPercent : 0.95f, lowPercent, 1.0f);
        float keyValue = Math.Max(float.IsFinite(_settings.KeyValue) ? _settings.KeyValue : 0.18f, 1e-4f);
        float compensation = float.IsFinite(_settings.ExposureCompensation) ? _settings.ExposureCompensation : 0.0f;
        float speedUp = Math.Max(float.IsFinite(_settings.SpeedUp) ? _settings.SpeedUp : 3.0f, 0.0f);
        float speedDown = Math.Max(float.IsFinite(_settings.SpeedDown) ? _settings.SpeedDown : 1.0f, 0.0f);
        float dt = float.IsFinite(context.DeltaTime) ? Math.Clamp(context.DeltaTime, 0.0f, 0.25f) : 0.0f;

        uint flags = (forceReset ? FlagForceReset : 0u)
            | (_settings.IgnoreDarkFloor ? FlagIgnoreDarkFloor : 0u);

        GpuBuffer histogram = context.GetBuffer(_frameHistogramBuffer);
        GpuBuffer state = context.GetBuffer(_frameStateBuffer);
        AutoExposureAdaptRoot root = new()
        {
            Histogram = histogram.View,
            State = state.View,
            MinLogLuminance = minLogLum,
            LogLuminanceRange = logLumRange,
            LowPercent = lowPercent,
            HighPercent = highPercent,
            MinEV = minEV,
            MaxEV = maxEV,
            ExposureCompensation = compensation,
            KeyValue = keyValue,
            SpeedUp = speedUp,
            SpeedDown = speedDown,
            DeltaTime = dt,
            Flags = flags,
        };

        using RenderContext.ComputeScope compute = rc.BeginCompute();
        compute.Dispatch(_adaptShader, in root, new Extent3D(1u, 1u, 1u));
        _hasEverExecuted = true;
    }

    private void ExecuteReadback(in RenderGraphContext context)
    {
        if ((uint)_frameReadbackSlotIndex >= (uint)_readbackSlots.Length)
        {
            return;
        }

        RenderContext rc = context.RenderContext;
        GpuBuffer state = context.GetBuffer(_frameStateBuffer);
        GpuBuffer histogram = context.GetBuffer(_frameHistogramBuffer);
        GpuBuffer readback = context.GetBuffer(_frameReadbackBuffer);

        ulong stateBytes = (ulong)sizeof(AutoExposureState);
        rc.CopyBuffer(state, 0, readback, 0, stateBytes);
        rc.CopyBuffer(histogram, 0, readback, stateBytes, HistogramBufferBytes);

        ref ReadbackSlot slot = ref _readbackSlots[_frameReadbackSlotIndex];
        slot.Context = rc;
        slot.FrameId = rc.CurrentFrameId;
        slot.DiscardVersion = rc.DiscardVersion;
        slot.Pending = true;
    }

    private void PollTelemetry()
    {
        for (int i = 0; i < _readbackSlots.Length; ++i)
        {
            ref ReadbackSlot slot = ref _readbackSlots[i];
            if (!slot.Pending)
            {
                continue;
            }

            if (slot.Context == null || slot.Context.IsDisposed || slot.Context.DiscardVersion != slot.DiscardVersion)
            {
                slot.Pending = false;
                slot.Context = null;
                slot.FrameId = 0;
                continue;
            }

            if (!slot.Context.IsFrameCompleted(slot.FrameId))
            {
                continue;
            }

            GpuBuffer buffer = slot.Buffer!;
            AutoExposureState readState = default;
            buffer.GetData(MemoryMarshal.CreateSpan(ref readState, 1), 0);
            if (readState.Initialized != 0u)
            {
                _lastState = readState;
                buffer.GetData<uint>(_lastHistogram, (ulong)sizeof(AutoExposureState));
                _hasTelemetry = true;
            }

            slot.Pending = false;
            slot.Context = null;
            slot.FrameId = 0;
        }
    }

    private int AcquireFreeReadbackSlot()
    {
        for (int i = 0; i < _readbackSlots.Length; ++i)
        {
            if (_readbackSlots[i].Pending)
            {
                continue;
            }

            _readbackSlots[i].Buffer ??= _rendering.CreateBuffer(
                (ulong)sizeof(AutoExposureState) + HistogramBufferBytes,
                MemoryType.Readback);
            return i;
        }

        return -1;
    }

    private void GetNormalizedLogLuminanceRange(out float minLogLum, out float logLumRange)
    {
        float rawMin = float.IsFinite(_settings.MinLogLuminance) ? _settings.MinLogLuminance : -10.0f;
        float rawMax = float.IsFinite(_settings.MaxLogLuminance) ? _settings.MaxLogLuminance : 14.0f;
        if (rawMin > rawMax)
        {
            (rawMin, rawMax) = (rawMax, rawMin);
        }

        minLogLum = rawMin;
        logLumRange = Math.Max(rawMax - rawMin, 0.25f);
    }

    private static Shader LoadComputeShader(ShaderSystem shaderSystem, string moduleName)
    {
        return shaderSystem.GetShaderFromSource(
            moduleName,
            moduleName + ".slang",
            LoadEmbeddedShaderSource(moduleName + ".slang"),
            ShaderKind.Compute,
            moduleName);
    }

    private static void VerifyRootLayout<TRoot>(Shader shader, string rootTypeName) where TRoot : unmanaged
    {
        if ((uint)sizeof(TRoot) != shader.Root.SizeBytes || !shader.Reflection.MatchesStructLayout<TRoot>(rootTypeName))
        {
            throw new InvalidOperationException(
                $"Shader '{shader.Name}' root struct '{rootTypeName}' ({shader.Root.SizeBytes}B) does not match {typeof(TRoot).Name} ({sizeof(TRoot)}B).");
        }
    }

    private static string LoadEmbeddedShaderSource(string logicalName)
    {
        Assembly assembly = typeof(RGNode_AutoExposure).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException($"Embedded shader '{logicalName}' was not found in {assembly.FullName}.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
