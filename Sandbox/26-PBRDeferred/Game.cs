using System.Diagnostics;
using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
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
    private readonly List<double> _flyRecordMs = [];
    private BistroImporter? _importer;
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
            scene = new World3DScene(RenderingSystem);
            scene.VoxelGi.Enabled = options.GiEnabled;
            scene.VoxelGi.BootstrapFullVolumeOnReset = options.BootstrapGi || (options.Frames.HasValue && !options.FlyStress);
            if (options.RsmEnabled.HasValue)
                scene.Shadows.Rsm.Enabled = options.RsmEnabled.Value;
            scene.LightingDebugView = options.GiDebugView;
            _demoLightsEnabled = options.AddDemoLights;
            _offscreen = setting.Platform is ConsolePlatform ? RenderingSystem.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(options.Width, options.Height, 1),
                Format = Format.Rgba8Srgb,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            }) : null;
        }
        catch
        {
            scene?.Dispose();
            context?.Dispose();
            base.Dispose();
            throw;
        }
        _scene = scene;
        _context = context;
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
                UpdateFlyStressCamera(_renderedFrames);
            long startStamp = Stopwatch.GetTimestamp();
            if (!TryRecordFrame(output, _offscreen != null ? RGUsage.TransferRead : RGUsage.ColorAttachment, delta))
            {
                Fail("World3D rejected the camera or output viewport.");
                return;
            }
            if (_options.FlyStress && _renderedFrames >= 1)
                _flyRecordMs.Add(Stopwatch.GetElapsedTime(startStamp).TotalMilliseconds);
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
            _importer?.Dispose();
            _scene.Dispose();
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

            ImGui.Text($"{GraphicsDevice.ActiveBackend}  |  {viewport.X} x {viewport.Y}  |  {delta * 1000f:F1} ms");
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
            if (ImGui.CollapsingHeader("VoxelGI (Clipmap)", ImGuiTreeNodeFlags.DefaultOpen))
                DrawVoxelGiControls();
            if (ImGui.CollapsingHeader("Occlusion (Hi-Z)", ImGuiTreeNodeFlags.DefaultOpen))
                DrawOcclusionControls();
            if (ImGui.CollapsingHeader("Shadows & Far Field", ImGuiTreeNodeFlags.DefaultOpen))
                DrawShadowControls();
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
            "None\0Cascade Index\0Shadow Factor\0Min-Max Early-Out\0Far Shadow Blend\0Hi-Z Mip0\0RSM Flux\0GI Diffuse\0GI Specular\0GI Visibility\0GI Raw Trace\0"))
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
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using FileStream png = File.Create(path);
            TextureUtility.EncodeTextureToPNG(RenderingSystem, _offscreen!, png);
            Log.Info($"Captured submitted World3D frame to '{path}'.");
        }
        _completed = true;
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
