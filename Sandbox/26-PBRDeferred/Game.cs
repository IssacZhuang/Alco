using System.Diagnostics;
using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.ImGUI;
using Alco.Rendering;
using Alco.World3D;

/// <summary>Hosts a World3D deferred scene with a fly camera in windowed mode and optional GPU capture.</summary>
internal sealed class Game : GameEngine
{
    private const float MouseLookRadiansPerCount = 0.008f / 5f;
    private const float MaximumPitch = 1.55f;
    private const float MinimumFlySpeed = 0.1f;
    private const float MaximumFlySpeed = 200f;

    private readonly SandboxOptions _options;
    private readonly string? _bistroDirectory;
    private readonly World3DCamera _initialCamera;
    private readonly Extent2D _initialExtent;
    private readonly RenderContext _context;
    private readonly World3DScene _scene;
    private readonly Texture? _offscreen;
    private readonly RenderingMemoryStats _preSceneMemory;
    private readonly bool _collectTimings;
    private readonly List<double> _recordCpuMs = [];
    private readonly List<double> _prepareAndRecordCpuMs = [];
    private readonly List<double> _localLightCpuMs = [];
    private readonly List<double> _graphGpuMs = [];
    private readonly SortedDictionary<string, List<double>> _passGpuMs = new(StringComparer.Ordinal);
    private readonly float[] _autoExposurePlot = new float[64];
    private readonly World3DPointLight[] _pointLights;
    private readonly string[] _pointLightNames;
    private BistroImporter? _importer;
    private LargeWorldStressScene? _largeWorld;
    private ImGUISystem? _imGui;
    private World3DCamera _camera;
    private float _flySpeed = 3f;
    private float _sunHour = 10f;
    private bool _showImGui = true;
    private int _selectedPointLight;
    private bool _pointLightsEnabled;
    private bool _pointLightsCastShadows;
    private bool _pointLightsAffectVoxelGi;
    private bool _pointLightsDirty;
    private WorldGeometryHandle _proceduralGeometry;
    private Extent2D _previousViewport;
    private ulong _firstMeasuredFrameId;
    private ulong _lastMeasuredFrameId;
    private ulong _sampledGpuFrameId;
    private World3DLocalLightClusterStats _sampledClusterStats;
    private uint _peakCoarseDrops;
    private uint _peakFineDrops;
    private int _clusterSampleCount;
    private int _minTimedPasses = int.MaxValue;
    private int _maxTimedPasses;
    private int _peakLightPatches;
    private int _renderedFrames;
    private bool _queuedProcedural;
    private bool _ready;
    private bool _failed;
    private bool _completed;

    /// <summary>Creates the engine, scene and graphics resources needed by the selected host.</summary>
    public Game(GameEngineSetting setting, SandboxOptions options, string? bistroDirectory, World3DCamera camera) : base(setting)
    {
        _options = options;
        _bistroDirectory = bistroDirectory;
        _initialCamera = _camera = camera;
        _initialExtent = new Extent2D(options.Width, options.Height);
        _collectTimings = options.Frames.HasValue && (options.FlyStress || options.LocalLightsMode.HasValue || options.ProfileGpu);
        RenderContext? context = null;
        World3DScene? scene = null;
        try
        {
            _preSceneMemory = RenderingSystem.GetMemoryStats();
            ulong frameDataBytes = 8 * 1024 * 1024;
            if (options.LocalLightsMode.HasValue)
            {
                // Reserve the same staging capacity in every mode for initial light rows and retirement scatters.
                ulong side = (ulong)options.StreamingRadius * 2 + 1;
                frameDataBytes += (side * side * (ulong)options.LightsPerSector + (ulong)options.OffscreenLights) * 96;
            }
            context = new RenderContext(RenderingSystem, frameDataByteCount: frameDataBytes);
            _offscreen = setting.Platform is ConsolePlatform ? RenderingSystem.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(options.Width, options.Height, 1),
                Format = Format.Rgba8Srgb,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            }) : null;
            scene = new World3DScene(RenderingSystem);
            scene.GpuProfilingEnabled = options.ProfileGpu;
            scene.LocalLights.EnableOverflowTelemetry = options.LocalLightsMode.HasValue && GraphicsDevice.ActiveBackend != GraphicsBackend.NoGPU;
            scene.VoxelGi.Enabled = options.GiEnabled;
            scene.VolumetricClouds.Enabled = options.CloudsEnabled;
            scene.VolumetricLight.Enabled = options.VolLightEnabled;
            scene.Ssr.Enabled = options.SsrEnabled;
            scene.Forward.Enabled = options.ForwardEnabled;
            scene.AutoExposure.Enabled = options.AutoExposureEnabled;
            scene.VoxelGi.BootstrapFullVolumeOnReset = options.BootstrapGi || (options.Frames.HasValue && !options.FlyStress);
            if (options.RsmEnabled.HasValue)
                scene.Shadows.Rsm.Enabled = options.RsmEnabled.Value;
            scene.LightingDebugView = options.GiDebugView;
            (string Name, World3DPointLight Light)[] presets = bistroDirectory != null ? CreateBistroLightRig() : options.Procedural
                ?
                [
                    ("Demo light 1", new(new World3DPosition(-4.0, 2.5, 3.2), new Vector3(1.0f, 0.72f, 0.38f), 22f, 12f)),
                    ("Demo light 2", new(new World3DPosition(4.5, 5.8, 2.3), new Vector3(1.0f, 0.78f, 0.45f), 16f, 10f)),
                    ("Demo light 3", new(new World3DPosition(-14.0, 0.0, 3.8), new Vector3(1.0f, 0.68f, 0.32f), 28f, 14f)),
                ]
                : [];
            _pointLights = Array.ConvertAll(presets, static preset => preset.Light);
            _pointLightNames = Array.ConvertAll(presets, static preset => preset.Name);
            if (bistroDirectory != null)
                scene.LocalLights.MaxWideViewLights = presets.Length;
            _pointLightsEnabled = options.PointLightsEnabled;
            // Bistro's emissive shades already contribute to VoxelGI.
            _pointLightsAffectVoxelGi = options.Procedural;
            _pointLightsDirty = _pointLightsEnabled;
        }
        catch
        {
            scene?.Dispose();
            _offscreen?.Dispose();
            context?.Dispose();
            base.Dispose();
            throw;
        }
        _scene = scene;
        _context = context;
    }

    private static (string Name, World3DPointLight Light)[] CreateBistroLightRig()
    {
        Vector3 signWhite = new(1f, 0.95f, 0.88f);
        Vector3 lanternAmber = new(1f, 0.51f, 0.22f);
        Vector3 pendantWhite = new(1f, 0.79f, 0.58f);
        Vector3 fanWarm = new(1f, 0.84f, 0.55f);
        return
        [
            ("Front sign lamp A", FixtureLight(new(4.315, -1.229, 4.063), signWhite, 1.8f, 2.6f, 0.055f)),
            ("Front sign lamp B", FixtureLight(new(6.373, -0.120, 4.063), signWhite, 1.8f, 2.6f, 0.055f)),
            ("Corner sign lamp A", FixtureLight(new(-1.943, 1.263, 4.062), signWhite, 1.6f, 2.4f, 0.055f)),
            ("Corner sign lamp B", FixtureLight(new(-2.850, 3.557, 4.062), signWhite, 1.6f, 2.4f, 0.055f)),
            ("Corner sign lamp C", FixtureLight(new(-3.639, 5.551, 4.062), signWhite, 1.6f, 2.4f, 0.055f)),
            ("Corner sign lamp D", FixtureLight(new(-4.498, 7.725, 4.062), signWhite, 1.6f, 2.4f, 0.055f)),

            ("Front window lantern A", FixtureLight(new(4.721, -1.157, 2.410), lanternAmber, 5.6f, 3.9f, 0.12f)),
            ("Front window lantern B", FixtureLight(new(7.222, -0.085, 2.385), lanternAmber, 6.1f, 4.1f, 0.12f)),
            ("Corner window lantern A", FixtureLight(new(-2.246, 1.829, 2.420), lanternAmber, 5.0f, 3.8f, 0.12f)),
            ("Corner window lantern B", FixtureLight(new(-3.338, 4.587, 2.415), lanternAmber, 5.4f, 3.9f, 0.12f)),
            ("Corner window lantern C", FixtureLight(new(-4.222, 7.310, 2.392), lanternAmber, 5.4f, 3.8f, 0.12f)),

            ("Front street lamp", FixtureLight(new(4.190, -4.480, 4.090), lanternAmber, 14f, 7.0f, 0.15f)),
            ("Corner street lamp", FixtureLight(new(-4.761, -2.208, 4.110), lanternAmber, 12f, 6.5f, 0.15f)),
            ("Alley street lamp", FixtureLight(new(18.200, -20.978, 4.092), lanternAmber, 12f, 6.5f, 0.15f)),
            ("No. 11 door lantern L", FixtureLight(new(17.175, -28.469, 3.254), lanternAmber, 3.6f, 3.7f, 0.12f)),
            ("No. 11 door lantern R", FixtureLight(new(21.041, -26.846, 3.254), lanternAmber, 3.6f, 3.7f, 0.12f)),

            ("Dining pendant A", FixtureLight(new(4.207, 6.624, 2.390), pendantWhite, 6.5f, 4.0f, 0.13f)),
            ("Dining pendant B", FixtureLight(new(2.786, 7.224, 2.390), pendantWhite, 6.0f, 4.0f, 0.13f)),
            ("Dining pendant C", FixtureLight(new(1.920, 8.797, 2.390), pendantWhite, 5.0f, 3.7f, 0.13f)),
            ("Dining pendant D", FixtureLight(new(1.054, 10.372, 2.390), pendantWhite, 5.0f, 3.7f, 0.13f)),
            ("Dining dome lamp A", FixtureLight(new(8.492, 6.115, 2.890), pendantWhite, 4.6f, 3.8f, 0.12f)),
            ("Dining dome lamp B", FixtureLight(new(4.934, 4.250, 3.040), pendantWhite, 4.8f, 3.8f, 0.12f)),
            ("Dining dome lamp C", FixtureLight(new(0.504, 5.002, 3.040), pendantWhite, 4.8f, 3.8f, 0.12f)),
            ("Dining dome lamp D", FixtureLight(new(-1.001, 7.984, 3.040), pendantWhite, 4.8f, 3.8f, 0.12f)),
            ("Ceiling fan lamp A", FixtureLight(new(0.984, 3.400, 3.475), fanWarm, 3.4f, 3.5f, 0.10f)),
            ("Ceiling fan lamp B", FixtureLight(new(-0.881, 6.399, 3.475), fanWarm, 3.4f, 3.5f, 0.10f)),

            ("Dining wall lamp 1", FixtureLight(new(8.110, 1.919, 2.543), signWhite, 2.2f, 2.5f, 0.08f)),
            ("Dining wall lamp 2", FixtureLight(new(5.511, 0.503, 2.543), signWhite, 2.2f, 2.5f, 0.08f)),
            ("Dining wall lamp 3", FixtureLight(new(2.840, -0.945, 2.543), signWhite, 2.2f, 2.5f, 0.08f)),
            ("Dining wall lamp 4", FixtureLight(new(-0.552, 0.412, 2.543), signWhite, 2.2f, 2.5f, 0.08f)),
            ("Dining wall lamp 5", FixtureLight(new(-1.944, 3.359, 2.543), signWhite, 2.2f, 2.5f, 0.08f)),
            ("Dining wall lamp 6", FixtureLight(new(-3.050, 6.174, 2.543), signWhite, 2.2f, 2.5f, 0.08f)),
        ];
    }

    private static World3DPointLight FixtureLight(World3DPosition position, Vector3 color, float intensity, float range,
        float sourceRadius) => new(position, color, intensity, range) { SourceRadius = sourceRadius };

    /// <summary>Gets the process result, including incomplete requested verification.</summary>
    public int ExitCode => _failed || !_ready || _renderedFrames == 0 || (_options.Frames.HasValue && !_completed) ? 1 : 0;

    /// <inheritdoc />
    protected override void OnStart()
    {
        bool nativeBc = TextureUtility.IsBcSupported(RenderingSystem);
        bool softwareBc = _options.SoftwareBc || !nativeBc;
        Log.Info($"World3D backend: requested={_options.Backend}, active={GraphicsDevice.ActiveBackend}, " +
            $"BC-supported={nativeBc}, output={(_offscreen != null ? "offscreen" : "swapchain")}");
        if (GraphicsDevice.ActiveBackend == GraphicsBackend.NoGPU && (_options.RequireBistro || _options.CapturePath != null))
        {
            Fail("The active backend has no GPU; Bistro verification and capture require a real backend.");
            return;
        }
        if (MainView.Swapchain != null)
        {
            _imGui = new ImGUISystem(this);
            AddSystem(_imGui);
        }
        if (_options.LargeWorld)
        {
            Log.Info($"Large-World Stress Scene: origin=({_options.LargeWorldOffset.X:F0}, {_options.LargeWorldOffset.Y:F0}, {_options.LargeWorldOffset.Z:F0}), " +
                $"streamingRadius={_options.StreamingRadius}");
            if (_options.FlyStress)
                _camera = LargeWorldStressScene.ComputeFlyStressCamera(_options.LargeWorldOffset, 0);
            _largeWorld = new LargeWorldStressScene(RenderingSystem, _scene, _options, in _camera);
            if (_options.LocalLightsMode is LocalLightMode mode)
                LogComparisonSettings(mode);
            return;
        }
        if (_bistroDirectory == null)
        {
            if (!TryQueueProceduralScene())
                Fail("Could not create the explicitly requested procedural scene.");
            return;
        }

        Log.Info($"Bistro exterior and interior: '{_bistroDirectory}'; textures={(softwareBc ? "bounded software RGBA8" : "native BC mips")}; " +
            $"metallic={(_options.LegacyMetallic ? "Bistro legacy dielectric override" : "glTF standard")}");
        if (!BistroImporter.TryOpen(RenderingSystem, _scene, _bistroDirectory, softwareBc, _options.LegacyMetallic, out _importer, out string? error))
            Fail($"Bistro import failed: {error}");
    }

    /// <inheritdoc />
    protected override void OnUpdate(float delta)
    {
        if (_failed)
            return;
        try
        {
            bool hasImGuiFrame = _imGui?.Renderer.IsInFrame == true;
            bool captureKeyboard = hasImGuiFrame && ImGui.GetIO().WantCaptureKeyboard;
            if (!captureKeyboard && Input.IsKeyDown(KeyCode.Escape))
            {
                Stop();
                return;
            }
            if (!_ready && !PrepareScene())
                return;

            RenderGraphTarget output = _offscreen != null ? RenderGraphTarget.FromTexture(_offscreen) : MainPresenter.CurrentRenderGraphTarget;
            if (!output.IsValid)
            {
                ReleaseMouseLook();
                return;
            }
            if (hasImGuiFrame)
            {
                if (MainView.IsFocused && ImGui.IsKeyPressed(ImGuiKey.F1, false))
                    _showImGui = !_showImGui;
                UpdateFlyCamera(delta, ImGui.GetIO().WantCaptureMouse, captureKeyboard);
            }
            long prepareStart = Stopwatch.GetTimestamp();
            if (_options.FlyStress)
            {
                if (_largeWorld != null)
                    _camera = LargeWorldStressScene.ComputeFlyStressCamera(_options.LargeWorldOffset, _renderedFrames);
                else
                    UpdateFlyStressCamera(_renderedFrames);
            }
            _largeWorld?.PrepareFrame(in _camera, _renderedFrames);
            // The panel is drawn after recording, when scene edits are no longer allowed.
            ApplyPointLightChanges();
            _scene.AutoExposure.TelemetryEnabled = hasImGuiFrame && _showImGui && _scene.AutoExposure.Enabled;
            double prepareMs = Stopwatch.GetElapsedTime(prepareStart).TotalMilliseconds;
            float sceneDelta = _options.FlyStress || (_options.LocalLightsMode.HasValue && _offscreen != null) ? 1f / 60f : delta;
            if (!TryRecordFrame(output, _offscreen != null ? RGUsage.TransferRead : RGUsage.ColorAttachment, sceneDelta, out double recordMs))
            {
                Fail("World3D rejected the camera or output viewport.");
                return;
            }
            RecordTimingSample(prepareMs, recordMs);
            _largeWorld?.RecordFrameTelemetry(_renderedFrames, recordMs);
            if (_options.LocalLightsMode.HasValue && _largeWorld is { } city && !VerifyLightPopulation(city))
                return;
            World3DDrawStats draws = _scene.LastRecordedDraws;
            if (_options.RequireBistro && (draws.MdiCalls == 0 || draws.IndirectSlots == 0))
            {
                Fail("Required Bistro draw did not record any World3D indirect submissions.");
                return;
            }
            if (hasImGuiFrame && _showImGui)
                DrawImGuiPanel(delta, output.Extent, draws);
            if (_renderedFrames == 0)
                Log.Info($"World3D first render: viewport={output.Extent.X}x{output.Extent.Y}, " +
                    $"MDI calls={draws.MdiCalls}, indirect slots={draws.IndirectSlots}, point lights={_scene.LastRecordedLocalLights.ResidentLights}");
            else if (!_previousViewport.Equals(output.Extent))
                Log.Info($"World3D resized: viewport={output.Extent.X}x{output.Extent.Y}");
            _previousViewport = output.Extent;
            if (++_renderedFrames == _options.Frames)
                Stop();
        }
        catch (Exception ex)
        {
            Log.Error("[World3D sandbox update] ", ex);
            _failed = true;
            Stop();
        }
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        ReleaseMouseLook();
        // The presenter has already submitted the final recorded frame.
        if (!_failed && _renderedFrames == _options.Frames)
            CompleteVerification();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (IsDisposed)
            return;
        try
        {
            ReleaseMouseLook();
            RenderingSystem.WaitIdle();
            _largeWorld?.Dispose();
            _largeWorld = null;
            _importer?.Dispose();
            _scene.Dispose();
            _offscreen?.Dispose();
            _context.Dispose();
            RenderingSystem.WaitIdle();
            RenderingSystem.Tick();
            if (_options.LargeWorld && !RenderingSystem.IsDisposed)
            {
                RenderingMemoryStats post = RenderingSystem.GetMemoryStats();
                bool vramClean = post.LiveBuffers == _preSceneMemory.LiveBuffers
                    && post.LiveTextures == _preSceneMemory.LiveTextures
                    && post.LiveSamplers == _preSceneMemory.LiveSamplers
                    && post.GpuBufferPool.UsedBytes == _preSceneMemory.GpuBufferPool.UsedBytes
                    && post.CpuBufferPool.UsedBytes == _preSceneMemory.CpuBufferPool.UsedBytes
                    && post.TexturePool.UsedBytes == _preSceneMemory.TexturePool.UsedBytes;
                Log.Info($"[World3D VRAM Check] buffers={post.LiveBuffers}/{_preSceneMemory.LiveBuffers}, " +
                    $"textures={post.LiveTextures}/{_preSceneMemory.LiveTextures}, samplers={post.LiveSamplers}/{_preSceneMemory.LiveSamplers}, " +
                    $"LEAK_CHECK={(vramClean ? "PASS" : "FAIL")}");
                if (!vramClean)
                    _failed = true;
            }
        }
        finally
        {
            base.Dispose();
        }
    }

    private void DrawImGuiPanel(float delta, Extent2D viewport, World3DDrawStats draws)
    {
        ImGui.SetNextWindowPos(new Vector2(14, 14), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(420, 440), ImGuiCond.FirstUseEver);
        ImGuiWindowFlags flags = Input.IsMouseRelativeMode ? ImGuiWindowFlags.NoMouseInputs : ImGuiWindowFlags.None;
        bool expanded = ImGui.Begin("PBR Deferred - World3D", ref _showImGui, flags);
        try
        {
            if (!expanded)
                return;

            int fps = FrameRate > 0 ? FrameRate : (delta > 0f ? (int)MathF.Round(1f / delta) : 0);
            float frameMs = FrameTime > 0f ? FrameTime * 1000f : delta * 1000f;
            ImGui.Text(FormattableString.Invariant($"{GraphicsDevice.ActiveBackend}  |  {viewport.X} x {viewport.Y}  |  {fps} FPS ({frameMs:F1} ms)"));
            ImGui.Text($"Instances: {_scene.InstanceCount}   MDI: {draws.MdiCalls}   Slots: {draws.IndirectSlots}");
            ImGui.Separator();
            ImGui.Text("RMB look / wheel speed, WASD move, Shift boost");
            ImGui.Text("E/Space up, Q/Ctrl down, F1 toggle panel");
            ImGui.SliderFloat("Fly speed", ref _flySpeed, MinimumFlySpeed, MaximumFlySpeed, "%.1f",
                ImGuiSliderFlags.Logarithmic | ImGuiSliderFlags.AlwaysClamp);
            if (ImGui.Button("Reset camera"))
                _camera = _initialCamera;
            ImGui.SameLine();
            if (ImGui.Button("Copy camera args"))
                CopyCameraArgs();
            World3DPosition eye = _camera.Eye;
            ImGui.Text(FormattableString.Invariant($"Eye: {eye.X:F3}, {eye.Y:F3}, {eye.Z:F3}"));
            ImGui.Separator();

            if (!_options.LargeWorld && ImGui.CollapsingHeader("Point Lights"))
                DrawPointLightControls();
            if (ImGui.CollapsingHeader("Lighting", ImGuiTreeNodeFlags.DefaultOpen))
                DrawLightingControls();
            if (ImGui.CollapsingHeader("Auto Exposure", ImGuiTreeNodeFlags.DefaultOpen))
                DrawAutoExposureControls();
            if (ImGui.CollapsingHeader("VoxelGI (Clipmap)", ImGuiTreeNodeFlags.DefaultOpen))
                DrawVoxelGiControls();
            if (ImGui.CollapsingHeader("Occlusion (Hi-Z)", ImGuiTreeNodeFlags.DefaultOpen))
                DrawOcclusionControls();
            if (ImGui.CollapsingHeader("Shadows & Far Field", ImGuiTreeNodeFlags.DefaultOpen))
                DrawShadowControls();
            if (ImGui.CollapsingHeader("Forward (Glass & Alpha)"))
                DrawForwardControls();
            if (ImGui.CollapsingHeader("Volumetric Clouds"))
                DrawVolumetricCloudsControls();
            if (ImGui.CollapsingHeader("Volumetric Light"))
                DrawVolumetricLightControls();
            if (ImGui.CollapsingHeader("Screen-Space Reflections"))
                DrawSsrControls();
        }
        finally
        {
            ImGui.End();
        }
    }

    private void CopyCameraArgs()
    {
        World3DPosition eye = _camera.Eye;
        Vector3 offset = Vector3.Transform(Vector3.UnitX, _camera.Rotation) * 10;
        Input.CopyToClipboard(FormattableString.Invariant($"--pos={eye.X:R},{eye.Y:R},{eye.Z:R} --look={eye.X + offset.X:R},{eye.Y + offset.Y:R},{eye.Z + offset.Z:R}"));
    }

    private void DrawLightingControls()
    {
        World3DLighting lighting = _scene.Lighting;
        float hour = _sunHour;
        bool sunChanged = ImGui.SliderFloat("Sun time (h)", ref hour, 5.5f, 18.5f, "%.2f", ImGuiSliderFlags.AlwaysClamp);
        float sunlight = lighting.SunIntensity;
        float exposure = lighting.SkyExposure;
        float ambient = lighting.AmbientFloor;
        bool changed = ImGui.SliderFloat("Sun intensity", ref sunlight, 0, 30, "%.2f", ImGuiSliderFlags.AlwaysClamp);
        changed |= ImGui.SliderFloat("Sky exposure", ref exposure, 0.1f, 4, "%.2f", ImGuiSliderFlags.AlwaysClamp);
        changed |= ImGui.SliderFloat("Ambient floor", ref ambient, 0, 2, "%.2f", ImGuiSliderFlags.AlwaysClamp);
        if (sunChanged)
        {
            _sunHour = hour;
            lighting = lighting with { DirectionToSun = ProceduralSkyUtility.GetDirectionToSun(hour) };
            changed = true;
        }
        if (changed)
            _scene.Lighting = lighting with { SunIntensity = sunlight, SkyExposure = exposure, AmbientFloor = ambient };
    }

    private void DrawAutoExposureControls()
    {
        World3DAutoExposureSettings ae = _scene.AutoExposure;
        bool enabled = ae.Enabled;
        bool awareBloom = ae.ExposureAwareBloom;
        if (ImGui.Checkbox("Auto exposure", ref enabled))
            ae.Enabled = enabled;
        ImGui.SameLine();
        if (ImGui.Checkbox("Exposure-aware bloom", ref awareBloom))
            ae.ExposureAwareBloom = awareBloom;

        bool halfRes = ae.HalfResolution;
        bool ignoreDark = ae.IgnoreDarkFloor;
        if (ImGui.Checkbox("Half-res metering", ref halfRes))
            ae.HalfResolution = halfRes;
        ImGui.SameLine();
        if (ImGui.Checkbox("Ignore dark floor", ref ignoreDark))
            ae.IgnoreDarkFloor = ignoreDark;
        ImGui.SameLine();
        if (ImGui.Button("Reset AE"))
            _scene.ResetAutoExposure();

        int metering = (int)ae.MeteringMode;
        if (ImGui.Combo("Metering mode", ref metering, "Uniform\0Center-Weighted\0Spot\0"))
            ae.MeteringMode = (AutoExposureMeteringMode)metering;
        if (ae.MeteringMode != AutoExposureMeteringMode.Uniform)
        {
            float radius = ae.CenterWeightRadius;
            if (ImGui.SliderFloat("Center radius", ref radius, 0.1f, 1.5f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
                ae.CenterWeightRadius = radius;
        }

        float compensation = ae.ExposureCompensation;
        float keyValue = ae.KeyValue;
        float minEv = ae.MinEV;
        float maxEv = ae.MaxEV;
        float lowPct = ae.LowPercent;
        float highPct = ae.HighPercent;
        float speedUp = ae.SpeedUp;
        float speedDown = ae.SpeedDown;

        if (ImGui.SliderFloat("EV compensation", ref compensation, -4f, 4f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            ae.ExposureCompensation = compensation;
        if (ImGui.SliderFloat("Key value", ref keyValue, 0.04f, 0.60f, "%.3f", ImGuiSliderFlags.AlwaysClamp))
            ae.KeyValue = keyValue;
        if (ImGui.SliderFloat("Min EV", ref minEv, -8f, 4f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            ae.MinEV = MathF.Min(minEv, ae.MaxEV);
        if (ImGui.SliderFloat("Max EV", ref maxEv, -2f, 12f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            ae.MaxEV = MathF.Max(maxEv, ae.MinEV);
        if (ImGui.SliderFloat("Low percent", ref lowPct, 0f, 0.95f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            ae.LowPercent = MathF.Min(lowPct, ae.HighPercent);
        if (ImGui.SliderFloat("High percent", ref highPct, 0.05f, 1f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            ae.HighPercent = MathF.Max(highPct, ae.LowPercent);
        if (ImGui.SliderFloat("Speed up", ref speedUp, 0.1f, 12f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            ae.SpeedUp = speedUp;
        if (ImGui.SliderFloat("Speed down", ref speedDown, 0.1f, 12f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            ae.SpeedDown = speedDown;

        World3DAutoExposureStats stats = _scene.LastRecordedAutoExposure;
        if (stats.HasTelemetry)
        {
            ImGui.Text(FormattableString.Invariant(
                $"Exposure: {stats.Exposure:F3}x  AdaptedEV: {stats.AdaptedEV:F2}  TargetEV: {stats.TargetEV:F2}  AvgLum: {stats.AverageLuminance:F3}"));

            ReadOnlySpan<uint> hist = _scene.LastAutoExposureHistogram;
            if (hist.Length == RGNode_AutoExposure.BinCount)
            {
                float maxVal = 1f;
                for (int i = 0; i < _autoExposurePlot.Length; ++i)
                {
                    int b = i * 4;
                    float sum = (float)hist[b] + hist[b + 1] + hist[b + 2] + hist[b + 3];
                    _autoExposurePlot[i] = sum;
                    if (sum > maxVal)
                        maxVal = sum;
                }
                ImGui.PlotHistogram(
                    "##AutoExposureHist",
                    ref _autoExposurePlot[0],
                    _autoExposurePlot.Length,
                    0,
                    "Log2 Luminance Histogram",
                    0f,
                    maxVal,
                    new Vector2(-1f, 64f));
            }
        }
    }

    private void DrawOcclusionControls()
    {
        World3DOcclusionSettings occSettings = _scene.Occlusion;
        bool enabled = occSettings.Enabled;
        bool twoPhase = occSettings.TwoPhase;
        if (ImGui.Checkbox("Hi-Z occlusion", ref enabled))
            occSettings.Enabled = enabled;
        ImGui.SameLine();
        if (ImGui.Checkbox("Two-phase retest", ref twoPhase))
            occSettings.TwoPhase = twoPhase;

        World3DOcclusionStats occ = _scene.LastRecordedOcclusion;
        ImGui.Text($"Hi-Z: {occ.HiZExtent.X}x{occ.HiZExtent.Y} ({occ.HiZMipCount} mips)");
        if (ImGui.Button("Sample GPU Occlusion Counts"))
            _scene.TryReadOcclusionCounts(out _, out _, out _);
        if (occ.HasReadbackCounts)
            ImGui.Text($"Phase1: {occ.Phase1VisibleDraws}  Occluded: {occ.OccludedCandidates}  Phase2: {occ.Phase2VisibleDraws}");
    }

    private void DrawShadowControls()
    {
        World3DShadowSettings shadows = _scene.Shadows;
        bool enabled = shadows.Enabled;
        bool minMax = shadows.UseMinMaxAcceleration;
        bool staticFar = shadows.FarField.EnableStaticFarCascade;
        bool farHeight = shadows.FarField.Enabled;
        bool autoCap = shadows.FarField.AutoCaptureFromScene;
        bool rsm = shadows.Rsm.Enabled;

        if (ImGui.Checkbox("Sun shadows", ref enabled))
            shadows.Enabled = enabled;
        ImGui.SameLine();
        if (ImGui.Checkbox("Min-Max pyramid", ref minMax))
            shadows.UseMinMaxAcceleration = minMax;

        float maxDist = shadows.MaxDistance;
        float tightness = shadows.ShadowTightness;
        if (ImGui.SliderFloat("CSM distance", ref maxDist, 30f, 600f, "%.0f m", ImGuiSliderFlags.AlwaysClamp))
            shadows.MaxDistance = maxDist;
        if (ImGui.SliderFloat("Shadow tightness", ref tightness, 0f, 1f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            shadows.ShadowTightness = tightness;

        if (ImGui.Checkbox("Static far cascade", ref staticFar))
            shadows.FarField.EnableStaticFarCascade = staticFar;
        ImGui.SameLine();
        if (ImGui.Checkbox("Far heightfield", ref farHeight))
            shadows.FarField.Enabled = farHeight;

        if (ImGui.Checkbox("Far auto-capture", ref autoCap))
            shadows.FarField.AutoCaptureFromScene = autoCap;
        ImGui.SameLine();
        if (ImGui.Checkbox("RSM pass", ref rsm))
            shadows.Rsm.Enabled = rsm;

        int debugView = (int)_scene.LightingDebugView;
        if (ImGui.Combo("Debug view", ref debugView,
            "None\0Cascade Index\0Shadow Factor\0Min-Max Early-Out\0Far Shadow Blend\0Hi-Z Mip0\0RSM Flux\0GI Diffuse\0GI Specular\0GI Visibility\0GI Raw Trace\0Cloud Opacity\0SSR Reflection\0SSR Confidence\0"))
            _scene.LightingDebugView = (World3DLightingDebugView)debugView;

        World3DShadowStats stats = _scene.LastRecordedShadows;
        ImGui.Text($"Res: {stats.CascadeResolution}  Updated: 0x{stats.UpdatedCascadeMask:X}  Cached: 0x{stats.CachedCascadeMask:X}  Draws: {stats.ShadowDrawCalls}");
        ImGui.Text($"MinMax: {stats.MinMaxActive}  StaticFar: {stats.StaticFarCascadeActive}  FarHeight: {stats.FarHeightfieldActive}  RSM: {stats.RsmUpdated}");
    }

    private void DrawVoxelGiControls()
    {
        World3DVoxelGiSettings gi = _scene.VoxelGi;
        bool enabled = gi.Enabled;
        bool bootstrap = gi.BootstrapFullVolumeOnReset;
        bool halfRes = gi.HalfResolutionTrace;
        if (ImGui.Checkbox("GI enabled", ref enabled))
            gi.Enabled = enabled;
        ImGui.SameLine();
        if (ImGui.Checkbox("Half-res trace", ref halfRes))
            gi.HalfResolutionTrace = halfRes;
        ImGui.SameLine();
        if (ImGui.Checkbox("Bootstrap reset", ref bootstrap))
            gi.BootstrapFullVolumeOnReset = bootstrap;

        float diff = gi.IndirectDiffuseIntensity;
        float spec = gi.IndirectSpecularIntensity;
        float emis = gi.EmissiveBoost;
        float decay = gi.PropagationDecay;
        if (ImGui.SliderFloat("Indirect diffuse", ref diff, 0f, 3f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            gi.IndirectDiffuseIntensity = diff;
        if (ImGui.SliderFloat("Indirect specular", ref spec, 0f, 3f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            gi.IndirectSpecularIntensity = spec;
        if (ImGui.SliderFloat("Emissive boost", ref emis, 0f, 4f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            gi.EmissiveBoost = emis;
        if (ImGui.SliderFloat("Propagation decay", ref decay, 0f, 1.2f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            gi.PropagationDecay = decay;

        if (ImGui.Button("Reset GI"))
            _scene.InvalidateVoxelGi();
        ImGui.SameLine();
        if (ImGui.Button("Sample GPU GI Counters"))
            _scene.TryReadVoxelGiCounters(out _);

        World3DVoxelGiStats stats = _scene.LastRecordedVoxelGi;
        ImGui.Text($"Res: {stats.Resolution}  Voxel: {stats.BaseVoxelSize:F2}m  Pool: {stats.StaticPoolCapacity}/{stats.DynamicPoolCapacity}  Lights: {stats.PointLightCount}");
        ImGui.Text($"Shift: 0x{stats.ShiftedCascadeMask:X}  Reset: 0x{stats.ResetCascadeMask:X}  Evicted: {stats.EvictedStaticBricksThisFrame}");
        World3DVoxelGiGpuCounters c = stats.LastReadbackCounters;
        ImGui.Text($"Used(S/D): {c.StaticUsedBricks}/{c.DynamicUsedBricks}  Fallback: {c.StaticFallbackBricks}/{c.DynamicFallbackBricks}  Chunks: {c.BrickDispatchChunks}");
    }

    private void DrawForwardControls()
    {
        World3DForwardSettings fwd = _scene.Forward;
        bool enabled = fwd.Enabled;
        bool hiz = fwd.UseHiZOcclusion;
        bool frontToBack = fwd.SortFrontToBack;
        bool refr = fwd.EnableRefraction;
        if (ImGui.Checkbox("Forward enabled", ref enabled)) fwd.Enabled = enabled;
        ImGui.SameLine();
        if (ImGui.Checkbox("Hi-Z cull", ref hiz)) fwd.UseHiZOcclusion = hiz;
        if (ImGui.Checkbox("Sort front-to-back", ref frontToBack)) fwd.SortFrontToBack = frontToBack;
        ImGui.SameLine();
        if (ImGui.Checkbox("Refraction", ref refr)) fwd.EnableRefraction = refr;
        float refrScale = fwd.RefractionScale;
        if (ImGui.SliderFloat("Refraction scale", ref refrScale, 0f, 0.2f, "%.3f", ImGuiSliderFlags.AlwaysClamp))
            fwd.RefractionScale = refrScale;
        World3DForwardStats stats = _scene.LastRecordedForward;
        ImGui.Text($"Candidates: {stats.CandidateCount}  Visible: {stats.VisibleDraws}  Buckets: {stats.BucketCount}  Refr: {stats.RefractionActive}");
    }

    private void DrawVolumetricCloudsControls()
    {
        World3DVolumetricCloudsSettings clouds = _scene.VolumetricClouds;
        bool enabled = clouds.Enabled;
        bool castShadows = clouds.CastShadows;
        if (ImGui.Checkbox("Clouds enabled", ref enabled)) clouds.Enabled = enabled;
        ImGui.SameLine();
        if (ImGui.Checkbox("Cloud shadows", ref castShadows)) clouds.CastShadows = castShadows;
        float coverage = clouds.Coverage;
        float density = clouds.Density;
        float shadowStrength = clouds.ShadowStrength;
        float temporal = clouds.TemporalBlend;
        if (ImGui.SliderFloat("Coverage", ref coverage, 0f, 1f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            clouds.Coverage = coverage;
        if (ImGui.SliderFloat("Density", ref density, 0.001f, 0.12f, "%.3f", ImGuiSliderFlags.AlwaysClamp))
            clouds.Density = density;
        if (ImGui.SliderFloat("Shadow strength", ref shadowStrength, 0f, 1f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            clouds.ShadowStrength = shadowStrength;
        if (ImGui.SliderFloat("Cloud temporal", ref temporal, 0f, 0.98f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            clouds.TemporalBlend = temporal;
        World3DVolumetricCloudsStats stats = _scene.LastRecordedVolumetricClouds;
        ImGui.Text($"Trace: {stats.TraceExtent.X}x{stats.TraceExtent.Y}  Shadow: {stats.ShadowExtent.X}x{stats.ShadowExtent.Y}  Hist: {stats.TemporalHistoryValid}");
    }

    private void DrawVolumetricLightControls()
    {
        World3DVolumetricLightSettings vl = _scene.VolumetricLight;
        bool enabled = vl.Enabled;
        bool half = vl.HalfResolution;
        if (ImGui.Checkbox("VolLight enabled", ref enabled)) vl.Enabled = enabled;
        ImGui.SameLine();
        if (ImGui.Checkbox("VolLight half-res", ref half)) vl.HalfResolution = half;
        float intensity = vl.Intensity;
        float anisotropy = vl.Anisotropy;
        float baseDensity = vl.BaseDensity;
        int steps = vl.StepCount;
        if (ImGui.SliderFloat("Shaft intensity", ref intensity, 0f, 3f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            vl.Intensity = intensity;
        if (ImGui.SliderFloat("Anisotropy", ref anisotropy, -0.9f, 0.95f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            vl.Anisotropy = anisotropy;
        if (ImGui.SliderFloat("Fog density", ref baseDensity, 0f, 0.15f, "%.3f", ImGuiSliderFlags.AlwaysClamp))
            vl.BaseDensity = baseDensity;
        if (ImGui.SliderInt("Shaft steps", ref steps, 4, 64))
            vl.StepCount = steps;
        World3DVolumetricLightStats stats = _scene.LastRecordedVolumetricLight;
        ImGui.Text($"Trace: {stats.TraceExtent.X}x{stats.TraceExtent.Y}  Steps: {stats.StepCount}");
    }

    private void DrawSsrControls()
    {
        World3DSsrSettings ssr = _scene.Ssr;
        bool enabled = ssr.Enabled;
        bool half = ssr.HalfResolution;
        bool disableFallback = ssr.DisableVoxelSpecularFallback;
        if (ImGui.Checkbox("SSR enabled", ref enabled)) ssr.Enabled = enabled;
        ImGui.SameLine();
        if (ImGui.Checkbox("SSR half-res", ref half)) ssr.HalfResolution = half;
        if (ImGui.Checkbox("Disable VoxelGI spec fallback", ref disableFallback))
            ssr.DisableVoxelSpecularFallback = disableFallback;
        float intensity = ssr.Intensity;
        float maxRough = ssr.MaxRoughness;
        float thickness = ssr.ThicknessMeters;
        float temporal = ssr.TemporalBlend;
        if (ImGui.SliderFloat("SSR intensity", ref intensity, 0f, 2f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            ssr.Intensity = intensity;
        if (ImGui.SliderFloat("SSR max roughness", ref maxRough, 0.05f, 1f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            ssr.MaxRoughness = maxRough;
        if (ImGui.SliderFloat("SSR thickness", ref thickness, 0.05f, 3f, "%.2f m", ImGuiSliderFlags.AlwaysClamp))
            ssr.ThicknessMeters = thickness;
        if (ImGui.SliderFloat("SSR temporal", ref temporal, 0f, 0.96f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            ssr.TemporalBlend = temporal;
        World3DSsrStats stats = _scene.LastRecordedSsr;
        ImGui.Text($"Trace: {stats.TraceExtent.X}x{stats.TraceExtent.Y}  HiZ: {stats.HiZActive}  Hist: {stats.TemporalHistoryValid}");
    }

    private void ApplyPointLightChanges()
    {
        if (!_pointLightsDirty)
            return;
        _pointLightsDirty = false;
        if (!_pointLightsEnabled)
        {
            _scene.SetPointLights([]);
            return;
        }

        for (int i = 0; i < _pointLights.Length; ++i)
        {
            _pointLights[i] = _pointLights[i] with
            {
                CastsShadow = _pointLightsCastShadows,
                AffectsGlobalIllumination = _pointLightsAffectVoxelGi,
            };
        }
        _scene.SetPointLights(_pointLights);
    }

    private void DrawPointLightControls()
    {
        _pointLightsDirty |= ImGui.Checkbox("Enable point lights", ref _pointLightsEnabled);
        ImGui.SameLine();
        ImGui.TextDisabled($"{_pointLights.Length} lights");
        bool flagsChanged = ImGui.Checkbox("Cast shadows (all lights)", ref _pointLightsCastShadows);
        flagsChanged |= ImGui.Checkbox("Affect Voxel GI (all lights)", ref _pointLightsAffectVoxelGi);
        _pointLightsDirty |= flagsChanged && _pointLightsEnabled;
        ImGui.PushItemWidth(-145f);
        try
        {
            DrawPointLightPicker();
            ImGui.PushID(_selectedPointLight);
            try
            {
                ref World3DPointLight light = ref _pointLights[_selectedPointLight];
                World3DPointLight edited = DrawPointLightEditor(in light);
                if (edited != light)
                {
                    light = edited;
                    _pointLightsDirty |= _pointLightsEnabled;
                }
            }
            finally
            {
                ImGui.PopID();
            }
        }
        finally
        {
            ImGui.PopItemWidth();
        }
    }

    private void DrawPointLightPicker()
    {
        if (!ImGui.BeginCombo("Emitter", _pointLightNames[_selectedPointLight], ImGuiComboFlags.HeightLarge))
            return;
        try
        {
            for (int i = 0; i < _pointLightNames.Length; i++)
            {
                bool selected = i == _selectedPointLight;
                if (ImGui.Selectable(_pointLightNames[i], selected))
                    _selectedPointLight = i;
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
        }
        finally
        {
            ImGui.EndCombo();
        }
    }

    private static World3DPointLight DrawPointLightEditor(in World3DPointLight light)
    {
        const ImGuiSliderFlags clamp = ImGuiSliderFlags.AlwaysClamp;
        Vector3 position = new((float)light.Position.X, (float)light.Position.Y, (float)light.Position.Z);
        bool moved = ImGui.DragFloat3("Position (m)", ref position, 0.05f, -10_000f, 10_000f, "%.2f", clamp);
        Vector3 color = light.Color;
        ImGui.ColorEdit3("Color (linear RGB)", ref color, ImGuiColorEditFlags.Float);
        float intensity = light.Intensity;
        ImGui.DragFloat("Intensity", ref intensity, 0.25f, 0f, 10_000f, "%.2f", clamp);
        float radius = light.Radius;
        ImGui.DragFloat("Range (m)", ref radius, 0.05f, 0.01f, 4096f, "%.2f", clamp);
        float sourceRadius = MathF.Min(light.SourceRadius, radius);
        ImGui.DragFloat("Emitter radius (m)", ref sourceRadius, 0.01f, 0f, radius, "%.3f", clamp);
        float shadowPriority = light.ShadowPriority;
        ImGui.DragFloat("Shadow priority", ref shadowPriority, 0.05f, 0f, 100f, "%.2f", clamp);
        return light with
        {
            Position = moved ? new World3DPosition(position.X, position.Y, position.Z) : light.Position,
            Color = Vector3.Clamp(color, Vector3.Zero, Vector3.One),
            Intensity = intensity,
            Radius = radius,
            SourceRadius = sourceRadius,
            ShadowPriority = shadowPriority,
        };
    }

    private void UpdateFlyStressCamera(int frameIndex)
    {
        double t = frameIndex * (1.0 / 60.0);
        double x = -12.0 + 18.0 * Math.Cos(t * 1.9);
        double y = 2.0 + 16.0 * Math.Sin(t * 1.4);
        double z = 3.5 + 1.5 * Math.Sin(t * 2.5);
        double dx = -18.0 * 1.9 * Math.Sin(t * 1.9);
        double dy = 16.0 * 1.4 * Math.Cos(t * 1.4);
        Vector3 forward = Vector3.Normalize(new Vector3((float)dx, (float)dy, -0.15f));
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, forward));
        Vector3 up = Vector3.Cross(forward, right);
        Quaternion rot = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            forward.X, forward.Y, forward.Z, 0f,
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            0f, 0f, 0f, 1f));
        _camera = new World3DCamera(new World3DPosition(x, y, z), rot, 0.83f, 0.1f);
    }

    private void UpdateFlyCamera(float delta, bool captureMouse, bool captureKeyboard)
    {
        if (!MainView.IsFocused)
        {
            ReleaseMouseLook();
            return;
        }

        bool wasLooking = Input.IsMouseRelativeMode;
        bool looking = Input.IsMousePressing(Mouse.Right) &&
            (wasLooking || (!captureMouse && Input.IsMouseDown(Mouse.Right)));
        if (!looking)
            ReleaseMouseLook();
        else if (!wasLooking)
        {
            Input.IsMouseRelativeMode = true;
            Input.IsCursorVisible = false;
        }
        else
        {
            Vector2 motion = Input.MouseDelta;
            if (motion != Vector2.Zero)
                RotateCamera(motion);
        }

        if (looking && Input.IsMouseScrolling(out Vector2 wheel))
            _flySpeed = Math.Clamp(_flySpeed * MathF.Pow(1.2f, wheel.Y), MinimumFlySpeed, MaximumFlySpeed);

        if (captureKeyboard || (captureMouse && !looking))
            return;

        int forward = (Input.IsKeyPressing(KeyCode.W) ? 1 : 0) - (Input.IsKeyPressing(KeyCode.S) ? 1 : 0);
        int right = (Input.IsKeyPressing(KeyCode.D) ? 1 : 0) - (Input.IsKeyPressing(KeyCode.A) ? 1 : 0);
        bool up = Input.IsKeyPressing(KeyCode.E) || Input.IsKeyPressing(KeyCode.Space);
        bool down = Input.IsKeyPressing(KeyCode.Q) || Input.IsKeyPressing(KeyCode.ControlLeft) || Input.IsKeyPressing(KeyCode.ControlRight);
        if (forward == 0 && right == 0 && up == down)
            return;

        Vector3 movement = Vector3.Transform(new Vector3(forward, right, 0), _camera.Rotation) +
            ((up ? 1 : 0) - (down ? 1 : 0)) * Vector3.UnitZ;
        if (movement == Vector3.Zero)
            return;

        float speed = Input.IsKeyPressing(KeyCode.ShiftLeft) || Input.IsKeyPressing(KeyCode.ShiftRight) ? _flySpeed * 4 : _flySpeed;
        double distance = (double)speed * delta;
        Vector3 direction = Vector3.Normalize(movement);
        World3DPosition eye = _camera.Eye;
        World3DPosition next = new(eye.X + direction.X * distance, eye.Y + direction.Y * distance, eye.Z + direction.Z * distance);
        if (World3DSceneCoordinates.TrySplitPosition(next, out _, out _))
            _camera = _camera with { Eye = next };
    }

    private void RotateCamera(Vector2 motion)
    {
        Vector3 forward = Vector3.Transform(Vector3.UnitX, _camera.Rotation);
        float horizontal = new Vector2(forward.X, forward.Y).Length();
        Vector3 right = Vector3.Transform(Vector3.UnitY, _camera.Rotation);
        float yaw = horizontal > 1e-6f ? MathF.Atan2(forward.Y, forward.X) : MathF.Atan2(-right.X, right.Y);
        yaw += motion.X * MouseLookRadiansPerCount;
        float pitch = Math.Clamp(MathF.Atan2(forward.Z, horizontal) - motion.Y * MouseLookRadiansPerCount,
            -MaximumPitch, MaximumPitch);
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -pitch);
        _camera = _camera with { Rotation = rotation };
    }

    private void ReleaseMouseLook()
    {
        if (!Input.IsMouseRelativeMode)
            return;
        Input.IsMouseRelativeMode = false;
        Input.IsCursorVisible = true;
    }

    private bool PrepareScene()
    {
        if (_largeWorld != null)
        {
            if (_largeWorld.HasPendingGeometryUploads)
            {
                if (!TryRecordFrame())
                    Fail("World3D rejected a Large-World geometry upload frame.");
                return false;
            }
            Log.Info($"Large-World ready: sectors={_largeWorld.ActiveSectorCount}, instances={_scene.InstanceCount}");
            _ready = true;
            return true;
        }
        if (_importer is BistroImporter importer)
        {
            if (!importer.AllQueued)
            {
                if (!importer.TryQueueNextBatch(out bool recordFrame, out string? error))
                    Fail($"Bistro upload failed: {error}");
                else if (recordFrame && !TryRecordFrame())
                    Fail("World3D rejected a Bistro geometry upload frame.");
                return false;
            }
            if (!importer.IsResident())
            {
                Fail("Bistro did not publish every imported geometry after submission.");
                return false;
            }
            Log.Info($"Bistro ready: {importer.Summary}, fixture lights={_pointLights.Length}");
            importer.Dispose();
            _importer = null;
        }
        else
        {
            if (!_queuedProcedural)
            {
                _queuedProcedural = TryRecordFrame();
                if (!_queuedProcedural)
                    Fail("World3D rejected the procedural geometry upload frame.");
                return false;
            }
            if (!_scene.IsGeometryLodResident(_proceduralGeometry, 0))
            {
                Fail("Procedural geometry was not published after submission.");
                return false;
            }
            Log.Info($"Procedural scene ready: explicitly requested, instances={_scene.InstanceCount}");
        }
        _ready = true;
        return true;
    }

    private bool TryRecordFrame() => TryRecordFrame(default, RGUsage.None, 1f / 60f, out _);

    private bool TryRecordFrame(RenderGraphTarget output, RGUsage outputFinalUsage, float deltaTime, out double recordMs)
    {
        recordMs = 0.0;
        _context.BeginFrame();
        long recordStart = Stopwatch.GetTimestamp();
        try
        {
            Extent2D viewport = output.IsValid ? output.Extent : _initialExtent;
            if (_scene.TryRecordFrame(_context, in _camera, viewport, out _, output, outputFinalUsage, renderWorld: true, deltaTime: deltaTime))
            {
                recordMs = Stopwatch.GetElapsedTime(recordStart).TotalMilliseconds;
                MainPresenter.EnqueueFrameContext(_context);
                return true;
            }
        }
        catch
        {
            _context.DiscardFrame();
            throw;
        }
        _context.DiscardFrame();
        return false;
    }

    private void LogComparisonSettings(LocalLightMode mode)
    {
        string modeName = mode switch
        {
            LocalLightMode.Direct => "direct",
            LocalLightMode.DirectWithShadows => "direct+shadow",
            _ => "off",
        };
        bool lit = mode != LocalLightMode.Off;
        var localLights = _scene.LocalLights;
        var pointShadows = _scene.PointShadows;
        Log.Info(FormattableString.Invariant($"[LocalLights run] mode={modeName}, backend={GraphicsDevice.ActiveBackend}, resolution={_initialExtent.X}x{_initialExtent.Y}, frames={_options.Frames}, warmup={_options.WarmupFrames}, fly={_options.FlyStress}, gpuTimings={_options.ProfileGpu}"));
        Log.Info(FormattableString.Invariant($"[LocalLights budgets] perSector={(lit ? _options.LightsPerSector : 0)}, offscreen={(lit ? _options.OffscreenLights : 0)}, view={localLights.MaxViewLights}, wide={localLights.MaxWideViewLights}, viewDepth={localLights.MaximumViewDepth}m, shadows={pointShadows.Enabled}, shadowFaces={pointShadows.MaxUpdatedFaces}, shadowTexels={pointShadows.MaxUpdatedTexels}, shadowCasters={pointShadows.MaxCasterCandidates}"));
    }

    private bool VerifyLightPopulation(LargeWorldStressScene city)
    {
        World3DLocalLightStats lights = _scene.LastRecordedLocalLights;
        if (lights.ResidentLights != city.ExpectedResidentLights)
        {
            Fail($"Local light residence mismatch: expected {city.ExpectedResidentLights}, recorded {lights.ResidentLights}.");
            return false;
        }
        if (_renderedFrames >= _options.WarmupFrames)
            _peakLightPatches = Math.Max(_peakLightPatches, _scene.LastRecordedSync.LocalLightPatches);
        if (_renderedFrames % 60 == 0 && _renderedFrames + 1 != _options.Frames)
            LogLocalLightStats(_renderedFrames + 1);
        return true;
    }

    private void LogLocalLightStats(int frame)
    {
        World3DLocalLightStats lights = _scene.LastRecordedLocalLights;
        Log.Info($"[LocalLights frame={frame}] sectors={_largeWorld?.ActiveSectorCount}, instances={_scene.InstanceCount}, " +
            $"resident={lights.ResidentLights}, queried={lights.QueriedLights}, visible={lights.VisibleLights}, " +
            $"selected={lights.SelectedLights}, dropped={lights.DroppedLights}, wideDropped={lights.WideLightsDropped}, " +
            $"patches={_scene.LastRecordedSync.LocalLightPatches}, peakPatches={_peakLightPatches}");
        World3DPointShadowStats shadows = _scene.LastRecordedPointShadows;
        Log.Info($"[PointShadows frame={frame}] cachedLights={shadows.CachedLights}, updatedFaces={shadows.UpdatedFaces}, " +
            $"updatedTexels={shadows.UpdatedTexels}, deferredFaces={shadows.DeferredFaces}, casterCandidates={shadows.CasterCandidates}, " +
            $"cacheHits={shadows.CacheHits}, cacheMisses={shadows.CacheMisses}, pageEvictions={shadows.PageEvictions}");
    }

    private void RecordTimingSample(double prepareMs, double recordMs)
    {
        if (!_collectTimings || _renderedFrames < _options.WarmupFrames)
            return;
        _recordCpuMs.Add(recordMs);
        _prepareAndRecordCpuMs.Add(prepareMs + recordMs);
        if (_scene.GpuProfilingEnabled)
            _localLightCpuMs.Add(_scene.LastRecordedLocalLightCpuMilliseconds);
        _lastMeasuredFrameId = _context.CurrentFrameId;
        if (_firstMeasuredFrameId == 0)
            _firstMeasuredFrameId = _lastMeasuredFrameId;
        CollectCompletedTelemetry();
    }

    private void CollectCompletedTelemetry()
    {
        if (_firstMeasuredFrameId == 0 || GraphicsDevice.ActiveBackend == GraphicsBackend.NoGPU)
            return;
        World3DLocalLightClusterStats clusters = _scene.LastCompletedLocalLightClusters;
        if (clusters.FrameId >= _firstMeasuredFrameId && clusters.FrameId <= _lastMeasuredFrameId &&
            clusters.FrameId > _sampledClusterStats.FrameId)
        {
            _sampledClusterStats = clusters;
            _peakCoarseDrops = Math.Max(_peakCoarseDrops, clusters.CoarseDroppedLights);
            _peakFineDrops = Math.Max(_peakFineDrops, clusters.FineDroppedLights);
            ++_clusterSampleCount;
        }

        ulong frame = _scene.GpuPassReportFrame;
        if (frame < _firstMeasuredFrameId || frame > _lastMeasuredFrameId || frame <= _sampledGpuFrameId)
            return;
        _sampledGpuFrameId = frame;
        ReadOnlySpan<RenderGraphPassReport> passes = _scene.GpuPassReports;
        _minTimedPasses = Math.Min(_minTimedPasses, passes.Length);
        _maxTimedPasses = Math.Max(_maxTimedPasses, passes.Length);
        double totalMs = 0.0;
        foreach (ref readonly RenderGraphPassReport pass in passes)
        {
            totalMs += pass.GpuMilliseconds;
            if (!IsComparisonPass(pass.Name))
                continue;
            if (!_passGpuMs.TryGetValue(pass.Name, out List<double>? times))
                _passGpuMs.Add(pass.Name, times = []);
            times.Add(pass.GpuMilliseconds);
        }
        _graphGpuMs.Add(totalMs);
    }

    private static bool IsComparisonPass(string name) =>
        name is "World3D.DeferredLighting" or "World3D.Forward.Draw" ||
        name.StartsWith("World3D.SceneSync.", StringComparison.Ordinal) ||
        name.StartsWith("World3D.LocalLights.", StringComparison.Ordinal) ||
        name.StartsWith("World3D.LocalLightClusters.", StringComparison.Ordinal) ||
        name.StartsWith("World3D.PointShadow.", StringComparison.Ordinal) ||
        name.StartsWith("World3D.PointShadows.", StringComparison.Ordinal);

    private void LogTimingSummary()
    {
        if (!_collectTimings)
            return;
        Log.Info($"[World3D timings] measured={_recordCpuMs.Count}/{_renderedFrames}, warmup={_options.WarmupFrames}, " +
            $"viewport={_previousViewport.X}x{_previousViewport.Y}; CPU samples exclude the frame-slot wait and submission");
        LogPercentiles("CPU record", _recordCpuMs);
        LogPercentiles("CPU prepare+record", _prepareAndRecordCpuMs);
        LogPercentiles("CPU local-light prepare", _localLightCpuMs);
        if (_scene.GpuProfilingEnabled)
        {
            if (_graphGpuMs.Count == 0)
                Log.Info("[GPU graph] timestamps unavailable on this run");
            else
            {
                Log.Info($"[GPU graph] completedFrames={_graphGpuMs.Count}/{_recordCpuMs.Count}, timedPassesPerFrame={_minTimedPasses}..{_maxTimedPasses}");
                LogPercentiles("GPU graph sum of timed passes", _graphGpuMs);
                foreach ((string name, List<double> times) in _passGpuMs)
                    LogPercentiles($"GPU {name}", times);
            }
        }
        if (_options.LocalLightsMode.HasValue)
        {
            World3DLocalLightClusterStats clusters = _sampledClusterStats;
            if (_clusterSampleCount == 0)
                Log.Info("[LocalLightClusters] no completed overflow sample");
            else
                Log.Info($"[LocalLightClusters] sampleFrameId={clusters.FrameId}, completedSamples={_clusterSampleCount}, " +
                    $"coarseOverflow={clusters.CoarseOverflowClusters}, coarseDropped={clusters.CoarseDroppedLights}, " +
                    $"fineOverflow={clusters.FineOverflowClusters}, fineDropped={clusters.FineDroppedLights}, " +
                    $"peakCoarseDropped={_peakCoarseDrops}, peakFineDropped={_peakFineDrops}");
        }
    }

    private static void LogPercentiles(string label, List<double> samples)
    {
        if (samples.Count == 0)
            return;
        samples.Sort();
        double mean = samples.Average();
        double p95 = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
        double p99 = samples[(int)Math.Ceiling(samples.Count * 0.99) - 1];
        Log.Info(FormattableString.Invariant($"[{label}] samples={samples.Count}, mean={mean:F3}ms, p95={p95:F3}ms, p99={p99:F3}ms, max={samples[^1]:F3}ms"));
    }

    private void CompleteVerification()
    {
        World3DDrawStats draws = _scene.LastRecordedDraws;
        int visible = 0;
        bool readCount = GraphicsDevice.ActiveBackend != GraphicsBackend.NoGPU && _scene.TryReadVisibleDrawCount(out visible);
        string visibleLabel = readCount ? visible.ToString() : "unavailable";
        Log.Info($"World3D verification: frames={_renderedFrames}, MDI calls={draws.MdiCalls}, " +
            $"indirect slots={draws.IndirectSlots}, visible draws={visibleLabel}");
        if (_options.RequireBistro && (!readCount || visible == 0))
        {
            Fail("Required Bistro verification did not read back any visible draw commands.");
            return;
        }
        if (GraphicsDevice.ActiveBackend != GraphicsBackend.NoGPU && _scene.VoxelGi.Enabled &&
            _scene.TryReadVoxelGiCounters(out World3DVoxelGiGpuCounters giCounters))
        {
            Log.Info($"World3D VoxelGI counters: staticUsed={giCounters.StaticUsedBricks}, dynUsed={giCounters.DynamicUsedBricks}, " +
                $"reclaimed={giCounters.StaticReclaimedThisFrame}, fallback={giCounters.StaticFallbackBricks}/{giCounters.DynamicFallbackBricks}, chunks={giCounters.BrickDispatchChunks}");
        }
        World3DForwardStats fwdStats = _scene.LastRecordedForward;
        World3DVolumetricCloudsStats cloudStats = _scene.LastRecordedVolumetricClouds;
        World3DVolumetricLightStats vlStats = _scene.LastRecordedVolumetricLight;
        World3DSsrStats ssrStats = _scene.LastRecordedSsr;
        Log.Info($"World3D Phase4: forward=({fwdStats.Enabled}, cand={fwdStats.CandidateCount}, buckets={fwdStats.BucketCount}), " +
            $"clouds=({cloudStats.Enabled}, shadow={cloudStats.CastShadows}, hist={cloudStats.TemporalHistoryValid}), " +
            $"volLight=({vlStats.Enabled}, {vlStats.TraceExtent.X}x{vlStats.TraceExtent.Y}), " +
            $"ssr=({ssrStats.Enabled}, hiz={ssrStats.HiZActive}, hist={ssrStats.TemporalHistoryValid})");
        if (GraphicsDevice.ActiveBackend != GraphicsBackend.NoGPU && _scene.AutoExposure.Enabled &&
            _scene.TryReadAutoExposureState(out AutoExposureState aeState))
        {
            World3DAutoExposureStats aeStats = _scene.LastRecordedAutoExposure;
            Log.Info($"World3D AutoExposure: enabled={aeStats.Enabled}, exp={aeState.Exposure:F3}, " +
                $"adaptedEV={aeState.AdaptedEV:F2}, targetEV={aeState.TargetEV:F2}, avgLum={aeState.AverageLuminance:F3}");
        }
        else
        {
            Log.Info($"World3D AutoExposure: enabled={_scene.LastRecordedAutoExposure.Enabled}");
        }
        if (_options.LocalLightsMode.HasValue)
            LogLocalLightStats(_renderedFrames);

        if (_options.CapturePath is string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (Path.GetDirectoryName(fullPath) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            using FileStream png = File.Create(fullPath);
            TextureUtility.EncodeTextureToPNG(RenderingSystem, _offscreen!, png);
            Log.Info($"Captured submitted World3D frame to '{fullPath}'.");
        }
        if (_largeWorld != null)
        {
            if (!_largeWorld.TeardownAndVerifyNoSceneLeaks(SubmitEmptyRetirementFrame, in _camera))
                _failed = true;
        }
        else if (_options.ProfileGpu && GraphicsDevice.ActiveBackend != GraphicsBackend.NoGPU)
        {
            // Resolve the two pending context slots without counting the drain frames.
            for (int i = 0; i < 2; ++i)
                if (!SubmitEmptyRetirementFrame(in _camera))
                    break;
        }
        LogTimingSummary();
        _completed = !_failed;
    }

    private bool SubmitEmptyRetirementFrame(in World3DCamera camera)
    {
        _context.BeginFrame();
        try
        {
            RenderGraphTarget target = _offscreen != null ? RenderGraphTarget.FromTexture(_offscreen) : default;
            RGUsage usage = _offscreen != null ? RGUsage.TransferRead : RGUsage.None;
            if (_scene.TryRecordFrame(_context, in camera, _initialExtent, out _, target, usage, renderWorld: false))
            {
                CollectCompletedTelemetry();
                _context.SubmitWithCompletion().Wait();
                RenderingSystem.Tick();
                return true;
            }
        }
        catch
        {
            _context.DiscardFrame();
            throw;
        }
        _context.DiscardFrame();
        return false;
    }

    private void Fail(string error)
    {
        Log.Error(error);
        _failed = true;
        Stop();
    }

    private bool TryQueueProceduralScene()
    {
        Vector3 min = new(-0.5f);
        Vector3 max = new(0.5f);
        if (!_scene.TryAttachSector(out WorldSectorHandle sector) || !_scene.TryRegisterGeometry(min, max, out _proceduralGeometry))
            return false;

        Span<VertexPBR> vertices = stackalloc VertexPBR[24];
        Span<uint> indices = stackalloc uint[36];
        ReadOnlySpan<Vector3> normals = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        for (int face = 0; face < normals.Length; face++)
        {
            Vector3 normal = normals[face];
            Vector3 u = MathF.Abs(normal.Z) > 0.5f ? Vector3.UnitX : Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, normal));
            Vector3 v = Vector3.Cross(normal, u);
            var tangent = new Vector4(u, 1);
            int vertex = face * 4;
            vertices[vertex] = new VertexPBR(0.5f * (normal - u - v), normal, Vector2.Zero, tangent);
            vertices[vertex + 1] = new VertexPBR(0.5f * (normal + u - v), normal, Vector2.UnitX, tangent);
            vertices[vertex + 2] = new VertexPBR(0.5f * (normal - u + v), normal, Vector2.UnitY, tangent);
            vertices[vertex + 3] = new VertexPBR(0.5f * (normal + u + v), normal, Vector2.One, tangent);
            int index = face * 6;
            uint first = (uint)vertex;
            indices[index] = first;
            indices[index + 1] = first + 1;
            indices[index + 2] = first + 2;
            indices[index + 3] = first + 1;
            indices[index + 4] = first + 3;
            indices[index + 5] = first + 2;
        }
        ModelGeometry geometry = ModelGeometry.Create<VertexPBR>(RenderingSystem, vertices, indices, min, max, "procedural cube");
        bool transferred = false;
        try
        {
            transferred = _scene.TryQueueGeometryLod(_proceduralGeometry, 0, geometry);
            if (!transferred)
                return false;
        }
        finally
        {
            if (!transferred)
                geometry.Dispose();
        }

        return TryAddBox(new World3DPosition(0, 0, -0.18), new Vector3(16, 16, 0.35f), new Vector3(0.28f, 0.32f, 0.38f), 0, 0.85f) &&
            TryAddBox(new World3DPosition(0, -2.2, 0.7), new Vector3(1.4f), new Vector3(0.85f, 0.18f, 0.08f), 0, 0.5f) &&
            TryAddBox(new World3DPosition(0, 0, 0.85), new Vector3(1.7f), new Vector3(1.0f, 0.7f, 0.22f), 0.9f, 0.28f) &&
            TryAddBox(new World3DPosition(0, 2.2, 0.7), new Vector3(1.4f), new Vector3(0.08f, 0.35f, 0.86f), 0.25f, 0.75f);

        bool TryAddBox(World3DPosition pivot, Vector3 scale, Vector3 color, float metallic, float roughness)
        {
            var material = new World3DMaterialDesc { BaseColor = new Vector4(color, 1), Metallic = metallic, Roughness = roughness };
            return _scene.TryRegisterMaterial(material, out WorldMaterialHandle handle) &&
                _scene.TryAddInstance(new World3DInstanceDesc(sector, _proceduralGeometry, handle,
                    new World3DTransform(pivot, Quaternion.Identity, scale)), out _);
        }
    }
}
