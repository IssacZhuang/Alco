using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.IO;
using Alco.Particles;
using Alco.Rendering;
using SandboxUtils;

/// <summary>
/// Sandbox demonstrating the 3D GPU particle system (Alco.Particles): particle
/// effect assets (<c>.apeff</c>, 3D flavor) simulated and rendered entirely on the
/// GPU, drawn as camera-facing billboards with depth testing against the scene.
/// The scene shows a looping flame jet, one-shot explosions (velocity-stretched
/// sparks plus a gradient-tinted, curve-grown smoke group) re-spawning constantly
/// (the frequent create/destroy path) and a vortex whose simulation comes from a
/// custom slang behavior module (SbVortex3D, local simulation space).
/// <br/>The ImGui panel manages the live instances: a click-selectable instance
/// list (dead instances are pruned automatically), per-effect spawn buttons
/// (spawned at the orbit target), delete, and — for the selected instance — a
/// scene gizmo (translate / rotate) plus live per-group parameter editing
/// (emission rate, lifetime, speed, size, gravity, tint, velocity stretch)
/// through the instance's params override API.
/// <br/>Controls: hold the middle mouse button to orbit (suspended while the
/// pointer is over the gizmo), drag the gizmo handles with the left button,
/// ESC to exit.
/// <br/>CLI: --screenshot=&lt;path.png&gt; [--frames=N]
/// </summary>
public class Game : GameEngine
{
    private readonly RenderPipeline _pipeline;
    private readonly CameraPerspectiveBuffer _camera;
    private readonly GpuParticleSystem3D _particles;
    private readonly Dictionary<string, ParticleEffect3DAsset> _effects = new();
    private readonly List<string> _effectNames = [];
    private readonly List<InstanceEntry> _instances = [];
    private FastRandom _random = new(54321);

    // The selected instance (panel selection + gizmo target); null when none.
    private InstanceEntry? _selected;
    private int _nextInstanceId = 1;

    // The gizmo operation of the selected instance: translate / rotate.
    private GizmoOperation _gizmoOperation = GizmoOperation.Translate;

    // Static scene: a ground slab and two pillars (depth-tested against particles).
    private readonly GraphicsMaterial _sceneMaterial;
    private readonly Mesh _cubeMesh;
    private readonly (Transform3D Transform, ColorFloat Color)[] _props;

    // Camera orbit state.
    private float _yaw = 0.35f;
    private float _pitch = 0.42f;
    private readonly float _distance = 20f;
    private readonly Vector3 _lookAt = new(1f, 0f, 2f);
    private const float RawMouseCountScale = 5f;

    // Auto spawner (frequent create/destroy exercise).
    private bool _autoSpawn = true;
    private float _autoSpawnInterval = 1.0f;
    private float _autoSpawnTimer;

    // Screenshot mode: the swapchain capture service reads the presented frame
    // (ImGui overlay included — a render-graph capture stops before it by design),
    // so the panel and the gizmo are part of the image.
    private readonly string? _screenshotPath;
    private readonly int _screenshotFrames;
    private int _frameCount;
    private SwapchainCaptureSystem? _swapchainCapture;
    private Task<RenderCaptureResult>? _screenshotRequest;
    private bool _screenshotSaved;

    public Game(GameEngineSetting setting, string[] args) : base(setting)
    {
        _screenshotPath = GetArgValue(args, "--screenshot=");
        _screenshotFrames = int.TryParse(GetArgValue(args, "--frames="), out int frames) ? frames : 90;
        if (_screenshotPath != null)
        {
            _swapchainCapture = new SwapchainCaptureSystem(this);
            AddSystem(_swapchainCapture);
        }

        AddSystem(new ImGUISystem(this));

        // Normal (non-reversed) depth: the plain pipeline clears depth to 1.
        _camera = RenderingSystem.CreateCameraPerspective(0.9f, 16f / 9, 0.1f, 300f);

        _particles = new GpuParticleSystem3D(RenderingSystem, particleCapacity: 1 << 18, emitterSlots: 512)
        {
            Camera = _camera,
            DepthStencilState = DepthStencilState.Read,
        };

        _pipeline = new RenderPipeline(RenderingSystem, new RenderPipeline.Descriptor
        {
            SceneLayout = RenderingSystem.PreferredHDRPass,
            BlitShader = BuiltInAssets.Shader_Blit,
            Width = MainView.Size.X,
            Height = MainView.Size.Y,
            Name = "gpu_particles_3d",
        });
        _pipeline.ClearColor = new ColorFloat(0.06f, 0.07f, 0.09f, 1);
        _pipeline.Use(new RGNode_Callback { Callback = context => _particles.RecordSimulation(in context) });
        _pipeline.Use(new SceneNode(this, _pipeline.Graph, _pipeline.Chain));
        var tonemapNode = new RGNode_Tonemap(
            RenderingSystem,
            _pipeline.Graph,
            _pipeline.Chain,
            _pipeline.PostProcessLayout,
            new RGNode_Tonemap.Descriptor
            {
                BlitShader = BuiltInAssets.Shader_Blit,
                ReinhardShader = BuiltInAssets.Shader_ReinhardLuminanceTonemap,
                Uncharted2Shader = BuiltInAssets.Shader_Uncharted2Tonemap,
                FilmicShader = BuiltInAssets.Shader_FilmicTonemap,
                AcesShader = BuiltInAssets.Shader_AcesTonemap,
                NeutralShader = BuiltInAssets.Shader_NeutralTonemap,
                AgxShader = BuiltInAssets.Shader_AgxTonemap,
            });
        tonemapNode.Operator = TonemapType.ACES;
        ACESTonemapData acesData = tonemapNode.ACESData;
        acesData.Gamma = 2.2f;
        tonemapNode.ACESData = acesData;
        _pipeline.Use(tonemapNode);

        MainPresenter.OnResize += size =>
        {
            _camera.AspectRatio = (float)size.X / size.Y;
            _pipeline.Resize(size.X, size.Y);
        };

        // Static scene props (unlit, depth-written so particles occlude correctly).
        _sceneMaterial = RenderingSystem.CreateGraphicsMaterial(BuiltInAssets.Shader_Unlit, "props");
        _sceneMaterial.SetBuffer("camera", _camera);
        _sceneMaterial.DepthStencilState = DepthStencilState.Write;
        _cubeMesh = RenderingSystem.MeshCube;

        Transform3D ground = Transform3D.Identity;
        ground.Position = new Vector3(0, 0, -0.5f);
        ground.Scale = new Vector3(80, 80, 1);
        Transform3D pillarA = Transform3D.Identity;
        pillarA.Position = new Vector3(0, 0, 3);
        pillarA.Scale = new Vector3(1.6f, 1.6f, 6);
        Transform3D pillarB = Transform3D.Identity;
        pillarB.Position = new Vector3(8, -5, 2);
        pillarB.Scale = new Vector3(2.4f, 2.4f, 4);
        _props =
        [
            (ground, new ColorFloat(0.16f, 0.17f, 0.2f, 1)),
            (pillarA, new ColorFloat(0.35f, 0.3f, 0.25f, 1)),
            (pillarB, new ColorFloat(0.25f, 0.3f, 0.35f, 1)),
        ];

        // The effect assets.
        _effects["Flare"] = AssetSystem.Load<ParticleEffect3DAsset>("Effects/Flare3D.apeff");
        _effects["Explosion"] = AssetSystem.Load<ParticleEffect3DAsset>("Effects/Explosion3D.apeff");
        _effects["Vortex"] = AssetSystem.Load<ParticleEffect3DAsset>("Effects/Vortex3D.apeff");
        _effectNames.AddRange(_effects.Keys);

        // The static scene (deterministic seeds keep screenshot mode reproducible).
        // The flare starts selected: it doubles as the gizmo showcase.
        _selected = Spawn("Flare", new Vector3(-6, -3, 0), 201);
        Spawn("Vortex", new Vector3(5, 3, 3), 202);
    }

    /// <summary>Registers the particle effect asset loader (.apeff) on top of the defaults.</summary>
    public override IEnumerable<IAssetLoader> CreateDefaultAssetLoaders()
    {
        foreach (IAssetLoader loader in base.CreateDefaultAssetLoaders())
        {
            yield return loader;
        }
        yield return new AssetLoaderParticleEffect(AssetSystem, RenderingSystem.ShaderSystem);
    }

    private InstanceEntry Spawn(string effectName, Vector3 position, int seed)
    {
        Transform3D transform = Transform3D.Identity;
        transform.Position = position;
        ParticleEffectInstance3D instance = _particles.CreateInstance(_effects[effectName], transform, seed);
        var entry = new InstanceEntry(instance, _nextInstanceId++);
        _instances.Add(entry);
        return entry;
    }

    // Spawns around the camera's orbit target so the new effect is immediately
    // visible — the manual spawn buttons use this, unlike the ambient
    // auto-spawner which keeps scattering explosions randomly.
    private InstanceEntry SpawnVisible(string effectName)
    {
        Vector3 position = _lookAt + new Vector3(
            _random.NextFloat(-1.5f, 1.5f),
            _random.NextFloat(-1.5f, 1.5f),
            _random.NextFloat(-0.5f, 1.5f));
        return Spawn(effectName, position, (int)_random.NextUint());
    }

    private InstanceEntry SpawnRandom(string effectName)
    {
        Vector3 position = new(
            _random.NextFloat(-4f, 12f),
            _random.NextFloat(-7f, 7f),
            _random.NextFloat(0f, 5f));
        return Spawn(effectName, position, (int)_random.NextUint());
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        UpdateCamera();

        if (_autoSpawn)
        {
            _autoSpawnTimer -= delta;
            if (_autoSpawnTimer <= 0)
            {
                _autoSpawnTimer = _autoSpawnInterval;
                SpawnRandom("Explosion");
            }
        }

        // Destroy instances whose particles have all died (frequent create/destroy).
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            if (!_instances[i].Instance.IsActive)
            {
                if (_instances[i] == _selected)
                {
                    _selected = null;
                }
                _instances[i].Instance.Dispose();
                _instances.RemoveAt(i);
            }
        }

        DrawImGuiPanel();
        DrawSelectionGizmo();

        DebugStats.Text(FrameRate);

        _frameCount++;
        if (_screenshotPath != null && _screenshotRequest == null && !_screenshotSaved && _frameCount >= _screenshotFrames)
        {
            // The request completes a couple of frames later with the presented
            // frame's PNG (scene, post processing and the ImGui overlay on top).
            _screenshotRequest = _swapchainCapture!.RequestCaptureAsync();
        }

        _pipeline.Render(MainPresenter.FrameBuffer);

        if (_screenshotRequest is { IsCompletedSuccessfully: true })
        {
            RenderCaptureResult result = _screenshotRequest.Result;
            if (result.Success && result.PngBytes != null)
            {
                File.WriteAllBytes(_screenshotPath!, result.PngBytes);
                Console.WriteLine($"Screenshot saved to {_screenshotPath}");
            }
            else
            {
                Console.WriteLine($"Screenshot failed: {result.Error}");
            }

            _screenshotSaved = true;
            Stop();
        }
        else if (_screenshotRequest is { IsFaulted: true })
        {
            Console.WriteLine($"Screenshot failed: {_screenshotRequest.Exception?.GetBaseException().Message}");
            Stop();
        }
    }

    private void UpdateCamera()
    {
        // The orbit yields to the gizmo: no camera rotation while the pointer is
        // over a handle or a handle drag is active (IsOver includes both).
        bool rotating = MainView.IsFocused && Input.IsMousePressing(Mouse.Middle) && !Gizmo.IsOver;
        Input.IsMouseRelativeMode = rotating;
        if (rotating)
        {
            Vector2 mouseDelta = Input.MouseDelta / RawMouseCountScale;
            _yaw += mouseDelta.X * 0.008f;
            _pitch = Math.Clamp(_pitch + mouseDelta.Y * 0.008f, -1.4f, 1.4f);
        }

        // Orbit: the camera sits on a sphere around the look-at point (engine
        // convention: forward = +X, right = +Y, up = +Z).
        Vector3 offset = new(
            -MathF.Cos(_pitch) * MathF.Cos(_yaw),
            -MathF.Cos(_pitch) * MathF.Sin(_yaw),
            MathF.Sin(_pitch));
        Vector3 position = _lookAt + offset * _distance;
        _camera.Transform = new Transform3D(position, LookRotation(_lookAt - position, Vector3.UnitZ));
        _camera.UpdateMatrixToGPU();
    }

    private void DrawImGuiPanel()
    {
        // Two columns, the editor's BeginChild + SameLine idiom: the instance list
        // with spawn/delete on the left, the selected instance's parameter editor
        // on the right. 'Appearing' (not 'FirstUseEver') so a stale imgui.ini
        // cannot shrink the window. The height stays modest so the window does not
        // cover the gizmo of the pre-selected flare near the bottom of the view.
        ImGui.SetNextWindowSize(new Vector2(680, 480), ImGuiCond.Appearing);
        ImGui.Begin("GPU Particles 3D");

        int groups = 0;
        foreach (InstanceEntry entry in _instances)
        {
            groups += entry.Instance.Asset.Groups.Count;
        }
        ImGui.Text($"Instances: {_instances.Count}   Emitter groups: {groups}");

        const float inspectorWidth = 340f;
        if (ImGui.BeginChild("##instances", new Vector2(-inspectorWidth, -1)))
        {
            // The live instances (click to select); dead ones are pruned in OnUpdate.
            ImGui.SeparatorText("Instances");
            foreach (InstanceEntry entry in _instances)
            {
                string state = entry.Instance.IsPlaying ? "playing" : "fading";
                if (ImGui.Selectable($"{entry.Instance.Asset.Name} #{entry.Id} ({state})", entry == _selected))
                {
                    _selected = entry;
                }
            }
            ImGui.BeginDisabled(_selected == null);
            if (ImGui.Button("Delete Selected") && _selected != null)
            {
                _selected.Instance.Dispose();
                _instances.Remove(_selected);
                _selected = null;
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Destroy All"))
            {
                foreach (InstanceEntry entry in _instances)
                {
                    entry.Instance.Dispose();
                }
                _instances.Clear();
                _selected = null;
            }

            // Manual spawns land at the orbit target so they are immediately visible.
            ImGui.SeparatorText("Spawn");
            foreach (string effectName in _effectNames)
            {
                if (ImGui.Button(effectName))
                {
                    _selected = SpawnVisible(effectName);
                }
            }

            ImGui.SeparatorText("Auto Spawn");
            ImGui.Checkbox("Auto Spawn Explosions", ref _autoSpawn);
            ImGui.SliderFloat("Spawn Interval", ref _autoSpawnInterval, 0.05f, 3.0f);
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // The selected instance: gizmo mode, numeric transform fallback and the
        // live per-group parameter overrides.
        if (ImGui.BeginChild("##selected", new Vector2(0, -1)))
        {
            if (_selected != null)
            {
                ParticleEffectInstance3D instance = _selected.Instance;
                ImGui.SeparatorText($"Selected: {instance.Asset.Name} #{_selected.Id}");

                ImGui.Text("Gizmo:");
                ImGui.SameLine();
                if (ImGui.RadioButton("Translate", _gizmoOperation == GizmoOperation.Translate))
                {
                    _gizmoOperation = GizmoOperation.Translate;
                }
                ImGui.SameLine();
                if (ImGui.RadioButton("Rotate", _gizmoOperation == GizmoOperation.Rotate))
                {
                    _gizmoOperation = GizmoOperation.Rotate;
                }

                Transform3D transform = instance.Transform;
                if (ImGui.EditTransform3D(ref transform))
                {
                    instance.Transform = transform;
                }

                for (int i = 0; i < instance.GroupCount; i++)
                {
                    if (ImGui.CollapsingHeader($"{instance.GetGroupName(i)}##group{i}", ImGuiTreeNodeFlags.DefaultOpen))
                    {
                        DrawGroupEditor(instance, i);
                    }
                }
            }
            else
            {
                ImGui.TextDisabled("Select an instance to edit");
            }
        }
        ImGui.EndChild();

        ImGui.End();
    }

    /// <summary>
    /// The selection gizmo: standard translate / rotate on the instance's 3D
    /// transform. Drawn outside the panel window so the handles are not clipped
    /// to it (the pattern of the 14-Collision / 27-Particles / 28-Gizmo sandboxes);
    /// the camera orbit yields to it (<see cref="UpdateCamera"/>).
    /// </summary>
    private void DrawSelectionGizmo()
    {
        if (_selected == null)
        {
            return;
        }
        Transform3D transform = _selected.Instance.Transform;
        if (Gizmo.Manipulate(
            _camera.Data.ViewMatrix,
            _camera.Data.ProjectionMatrix,
            _gizmoOperation,
            GizmoMode.Local,
            ref transform))
        {
            _selected.Instance.Transform = transform;
        }
    }

    /// <summary>
    /// Live editing of one emitter group of the selected instance: the practical
    /// scalar subset (emission rate, lifetime, speed, size, gravity, tint,
    /// velocity stretch). Reads the group's current record every frame and writes
    /// the edited one back through <see cref="ParticleEffectInstance3D.SetGroupParams"/>,
    /// which applies to the running instance without respawning it.
    /// </summary>
    private static void DrawGroupEditor(ParticleEffectInstance3D instance, int groupIndex)
    {
        ImGui.PushID(groupIndex);
        EmitterParams3D parameters = instance.GetGroupParams(groupIndex);
        bool edited = false;

        // CPU-side per-instance emission rate (future spawns).
        float rate = instance.GetGroupEmissionRate(groupIndex);
        if (ImGui.DragFloat("Emission Rate", ref rate, 1f, 0f, 5000f))
        {
            instance.SetGroupEmissionRate(groupIndex, rate);
        }

        Vector2 lifetime = new(parameters.Life.X, parameters.Life.Y);
        if (ImGui.DragFloat2("Lifetime", ref lifetime, 0.05f, 0.05f, 60f))
        {
            lifetime = new Vector2(Math.Min(lifetime.X, lifetime.Y), Math.Max(lifetime.X, lifetime.Y));
            parameters.Life = new Vector4(lifetime, parameters.Life.Z, parameters.Life.W);
            edited = true;
        }

        Vector2 speed = new(parameters.Speed.X, parameters.Speed.Y);
        if (ImGui.DragFloat2("Speed", ref speed, 0.1f, 0f, 200f))
        {
            speed = new Vector2(Math.Min(speed.X, speed.Y), Math.Max(speed.X, speed.Y));
            parameters.Speed = new Vector4(speed, parameters.Speed.Z, parameters.Speed.W);
            edited = true;
        }

        float sizeMin = parameters.Size.X;
        float sizeMax = parameters.Size.Y;
        if (ImGui.DragFloat("Size Min", ref sizeMin, 0.01f, 0.01f, 50f))
        {
            sizeMin = Math.Min(sizeMin, sizeMax);
            edited = true;
        }
        if (ImGui.DragFloat("Size Max", ref sizeMax, 0.01f, 0.01f, 50f))
        {
            sizeMax = Math.Max(sizeMin, sizeMax);
            edited = true;
        }
        parameters.Size = new Vector4(sizeMin, sizeMax, parameters.Size.Z, parameters.Size.W);

        // Gravity as a scale of the authored value (keeps its direction); groups
        // authored without gravity edit the vector directly.
        Vector3 assetGravity = instance.Asset.Groups[groupIndex].Gravity;
        Vector3 gravity = new(parameters.Motion.X, parameters.Motion.Y, parameters.Motion.Z);
        if (assetGravity.LengthSquared() > 1e-10f)
        {
            float gravityScale = gravity.Length() / assetGravity.Length();
            if (ImGui.DragFloat("Gravity Scale", ref gravityScale, 0.02f, -10f, 10f))
            {
                Vector3 scaled = assetGravity * gravityScale;
                parameters.Motion = new Vector4(scaled, parameters.Motion.W);
                edited = true;
            }
        }
        else if (ImGui.DragFloat3("Gravity", ref gravity, 0.05f))
        {
            parameters.Motion = new Vector4(gravity, parameters.Motion.W);
            edited = true;
        }

        ColorFloat tint = parameters.Tint;
        if (ImGui.ColorEdit4("Tint", ref tint))
        {
            parameters.Tint = tint;
            edited = true;
        }

        // Velocity stretch (3D billboards; no alignment prerequisite).
        bool stretch = (parameters.Flags & EmitterParams3D.FlagVelocityStretch) != 0u;
        if (ImGui.Checkbox("Velocity Stretch", ref stretch))
        {
            parameters.Flags = stretch
                ? parameters.Flags | EmitterParams3D.FlagVelocityStretch
                : parameters.Flags & ~EmitterParams3D.FlagVelocityStretch;
            edited = true;
        }
        if (stretch)
        {
            float lengthScale = parameters.Size.W;
            if (ImGui.DragFloat("Stretch Length Scale", ref lengthScale, 0.02f, 0f, 10f))
            {
                parameters.Size = new Vector4(parameters.Size.X, parameters.Size.Y, parameters.Size.Z, lengthScale);
                edited = true;
            }
            float speedScale = parameters.Speed.W;
            if (ImGui.DragFloat("Stretch Speed Scale", ref speedScale, 0.005f, 0f, 2f))
            {
                parameters.Speed = new Vector4(parameters.Speed.X, parameters.Speed.Y, parameters.Speed.Z, speedScale);
                edited = true;
            }
        }

        if (edited)
        {
            instance.SetGroupParams(groupIndex, parameters);
        }
        ImGui.PopID();
    }

    protected override void OnStop()
    {
        _particles.Dispose();
        _pipeline.Dispose();
    }

    private static string? GetArgValue(string[] args, string prefix)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg[prefix.Length..];
            }
        }
        return null;
    }

    private static Quaternion LookRotation(Vector3 forward, Vector3 worldUp)
    {
        forward = Vector3.Normalize(forward);
        Vector3 up = Vector3.Normalize(worldUp - forward * Vector3.Dot(forward, worldUp));
        Vector3 right = Vector3.Cross(up, forward);
        Matrix4x4 m = new(
            forward.X, forward.Y, forward.Z, 0,
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            0, 0, 0, 1);
        return Quaternion.CreateFromRotationMatrix(m);
    }

    /// <summary>The scene content node: static props (depth-written), then the particles.</summary>
    private sealed class SceneNode : RGNode_SceneContent
    {
        private readonly Game _game;

        public SceneNode(Game game, RenderGraph graph, RenderChain chain) : base(graph, chain)
        {
            _game = game;
        }

        protected override void OnRender(in RenderGraphContext context, GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            using (RenderPassScope pass = context.RenderContext.BeginPass(target))
            {
                foreach ((Transform3D transform, ColorFloat color) in _game._props)
                {
                    pass.DrawWithConstant(_game._cubeMesh, _game._sceneMaterial,
                        new PropConstant { Matrix = transform.Matrix, Color = color });
                }
                _game._particles.Render(pass);
            }
        }
    }

    /// <summary>The per-draw constant of the unlit scene props.</summary>
    private struct PropConstant
    {
        public Matrix4x4 Matrix;
        public ColorFloat Color;
    }

    /// <summary>
    /// A live instance plus its stable list id — the instance list prunes and
    /// reorders, the id keeps the panel labels unique and recognizable.
    /// </summary>
    private sealed class InstanceEntry(ParticleEffectInstance3D instance, int id)
    {
        public readonly ParticleEffectInstance3D Instance = instance;
        public readonly int Id = id;
    }
}
