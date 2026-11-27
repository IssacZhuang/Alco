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
    private BistroImporter? _importer;
    private ImGUISystem? _imGui;
    private World3DCamera _camera;
    private float _flySpeed = 3f;
    private bool _showImGui = true;
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
            if (!TryRecordFrame(output, _offscreen != null ? RGUsage.TransferRead : RGUsage.ColorAttachment))
            {
                Fail("World3D rejected the camera or output viewport.");
                return;
            }
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
        ImGui.SetNextWindowSize(new Vector2(400, 280), ImGuiCond.FirstUseEver);
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
        float sunlight = lighting.SunIntensity;
        float exposure = lighting.SkyExposure;
        float ambient = lighting.AmbientFloor;
        bool changed = ImGui.SliderFloat("Sun intensity", ref sunlight, 0, 30, "%.2f", ImGuiSliderFlags.AlwaysClamp);
        changed |= ImGui.SliderFloat("Sky exposure", ref exposure, 0.1f, 4, "%.2f", ImGuiSliderFlags.AlwaysClamp);
        changed |= ImGui.SliderFloat("Ambient floor", ref ambient, 0, 2, "%.2f", ImGuiSliderFlags.AlwaysClamp);
        if (changed)
            _scene.Lighting = lighting with { SunIntensity = sunlight, SkyExposure = exposure, AmbientFloor = ambient };
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

    private bool TryRecordFrame(RenderGraphTarget output = default, RGUsage outputFinalUsage = RGUsage.None)
    {
        _context.BeginFrame();
        try
        {
            Extent2D viewport = output.IsValid ? output.Extent : _initialExtent;
            if (_scene.TryRecordFrame(_context, in _camera, viewport, out _, output, outputFinalUsage))
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
