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
    private readonly List<double> _flyRecordMs = [];
    private readonly float[] _autoExposurePlot = new float[64];
    private BistroImporter? _importer;
    private LargeWorldStressScene? _largeWorld;
    private ImGUISystem? _imGui;
    private World3DCamera _camera;
    private float _flySpeed = 3f;
    private float _sunHour = 10f;
    private bool _showImGui = true;
    private bool _demoLightsEnabled;
    private WorldGeometryHandle _proceduralGeometry;
    private Extent2D _previousViewport;
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
        RenderContext? context = null;
        World3DScene? scene = null;
        try
        {
            context = new RenderContext(RenderingSystem, frameDataByteCount: 8 * 1024 * 1024);
            _offscreen = setting.Platform is ConsolePlatform ? RenderingSystem.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(options.Width, options.Height, 1),
                Format = Format.Rgba8Srgb,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            }) : null;
            _preSceneMemory = RenderingSystem.GetMemoryStats();
            scene = new World3DScene(RenderingSystem);
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
            _demoLightsEnabled = options.AddDemoLights;
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
        if (!options.LargeWorld)
            ApplyDemoPointLights(_demoLightsEnabled);
    }

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
            _largeWorld = new LargeWorldStressScene(RenderingSystem, _scene,
                _options.LargeWorldOffset, _options.StreamingRadius, GraphicsDevice.ActiveBackend != GraphicsBackend.NoGPU);
            _largeWorld.ConfigureAndPopulateInitial(in _camera);
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
            if (_options.FlyStress)
            {
                if (_largeWorld != null)
                    _camera = _largeWorld.ComputeFlyStressCamera(_renderedFrames);
                else
                    UpdateFlyStressCamera(_renderedFrames);
            }
            _largeWorld?.PrepareFrame(in _camera, _renderedFrames);
            _scene.AutoExposure.TelemetryEnabled = hasImGuiFrame && _showImGui && _scene.AutoExposure.Enabled;
            long startStamp = Stopwatch.GetTimestamp();
            if (!TryRecordFrame(output, _offscreen != null ? RGUsage.TransferRead : RGUsage.ColorAttachment, delta))
            {
                Fail("World3D rejected the camera or output viewport.");
                return;
            }
            double recordMs = Stopwatch.GetElapsedTime(startStamp).TotalMilliseconds;
            if (_options.FlyStress && _renderedFrames >= 1)
                _flyRecordMs.Add(recordMs);
            _largeWorld?.RecordFrameTelemetry(_renderedFrames, recordMs);
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
                    $"MDI calls={draws.MdiCalls}, indirect slots={draws.IndirectSlots}");
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
            _offscreen?.Dispose();
            _context.Dispose();
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

        if (ImGui.Checkbox("Demo point lights", ref _demoLightsEnabled))
            ApplyDemoPointLights(_demoLightsEnabled);
        ImGui.SameLine();
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

    private void ApplyDemoPointLights(bool enabled)
    {
        if (!enabled)
        {
            _scene.SetPointLights([]);
            return;
        }
        ReadOnlySpan<World3DPointLight> lights =
        [
            new(new World3DPosition(-4.0, 2.5, 3.2), new Vector3(1.0f, 0.72f, 0.38f), 22f, 12f),
            new(new World3DPosition(4.5, 5.8, 2.3), new Vector3(1.0f, 0.78f, 0.45f), 16f, 10f),
            new(new World3DPosition(-14.0, 0.0, 3.8), new Vector3(1.0f, 0.68f, 0.32f), 28f, 14f),
        ];
        _scene.SetPointLights(lights);
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
            Log.Info($"Bistro ready: {importer.Summary}");
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

    private bool TryRecordFrame(RenderGraphTarget output = default, RGUsage outputFinalUsage = RGUsage.None, float deltaTime = 1f / 60f)
    {
        _context.BeginFrame();
        try
        {
            Extent2D viewport = output.IsValid ? output.Extent : _initialExtent;
            if (_scene.TryRecordFrame(_context, in _camera, viewport, out _, output, outputFinalUsage, renderWorld: true, deltaTime: deltaTime))
            {
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
        if (_options.FlyStress && _flyRecordMs.Count > 0)
        {
            _flyRecordMs.Sort();
            double sum = 0;
            foreach (double v in _flyRecordMs)
                sum += v;
            double mean = sum / _flyRecordMs.Count;
            double p95 = _flyRecordMs[Math.Clamp((int)(_flyRecordMs.Count * 0.95), 0, _flyRecordMs.Count - 1)];
            double p99 = _flyRecordMs[Math.Clamp((int)(_flyRecordMs.Count * 0.99), 0, _flyRecordMs.Count - 1)];
            double max = _flyRecordMs[^1];
            Log.Info($"World3D FlyStress timings (samples={_flyRecordMs.Count}): mean={mean:F3}ms, p95={p95:F3}ms, p99={p99:F3}ms, max={max:F3}ms");
        }

        if (_options.CapturePath is string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (Path.GetDirectoryName(fullPath) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            using FileStream png = File.Create(fullPath);
            TextureUtility.EncodeTextureToPNG(RenderingSystem, _offscreen!, png);
            Log.Info($"Captured submitted World3D frame to '{fullPath}'.");
        }
        if (_largeWorld != null && !_largeWorld.TeardownAndVerifyNoSceneLeaks(
            _context,
            (in World3DCamera cam) => SubmitEmptyRetirementFrame(in cam),
            in _camera))
        {
            _failed = true;
            return;
        }
        _completed = true;
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
