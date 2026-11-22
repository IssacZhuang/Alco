using System.Numerics;
using System.Runtime.InteropServices;
using Alco;
using Alco.Effects;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.IO;
using Alco.Rendering;

/// <summary>Sandbox demonstrating the 3D GPU particle system with camera-facing billboards, depth-tested static props, and live parameter editing.</summary>
/// <remarks>Middle mouse orbits, left mouse drags the gizmo, and ESC exits; <c>--screenshot=&lt;path.png&gt;</c> captures a frame and <c>--particles=N</c> fills the pool.</remarks>
public unsafe class Game : GameEngine
{
    private const string PropsShaderSource = """
        #language slang 2025
        module SandboxPropsCube3D;
        import Core;

        public struct PropInstance
        {
            public float4x4 worldMatrix;
            public float4 color;
        };

        [GPURoot]
        public struct PropsRoot
        {
            public float4x4 viewProjection;
            public GPUBufferView instances;
        };

        public struct V2F
        {
            public float4 position : SV_POSITION;
            public float3 normal : TEXCOORD0;
            public float4 color : COLOR0;
        };

        static const float3 CubePositions[36] =
        {
            // +X
            float3( 0.5, -0.5, -0.5), float3( 0.5,  0.5, -0.5), float3( 0.5,  0.5,  0.5),
            float3( 0.5, -0.5, -0.5), float3( 0.5,  0.5,  0.5), float3( 0.5, -0.5,  0.5),
            // -X
            float3(-0.5,  0.5, -0.5), float3(-0.5, -0.5, -0.5), float3(-0.5, -0.5,  0.5),
            float3(-0.5,  0.5, -0.5), float3(-0.5, -0.5,  0.5), float3(-0.5,  0.5,  0.5),
            // +Y
            float3( 0.5,  0.5, -0.5), float3(-0.5,  0.5, -0.5), float3(-0.5,  0.5,  0.5),
            float3( 0.5,  0.5, -0.5), float3(-0.5,  0.5,  0.5), float3( 0.5,  0.5,  0.5),
            // -Y
            float3(-0.5, -0.5, -0.5), float3( 0.5, -0.5, -0.5), float3( 0.5, -0.5,  0.5),
            float3(-0.5, -0.5, -0.5), float3( 0.5, -0.5,  0.5), float3(-0.5, -0.5,  0.5),
            // +Z
            float3(-0.5, -0.5,  0.5), float3( 0.5, -0.5,  0.5), float3( 0.5,  0.5,  0.5),
            float3(-0.5, -0.5,  0.5), float3( 0.5,  0.5,  0.5), float3(-0.5,  0.5,  0.5),
            // -Z
            float3(-0.5,  0.5, -0.5), float3( 0.5,  0.5, -0.5), float3( 0.5, -0.5, -0.5),
            float3(-0.5,  0.5, -0.5), float3( 0.5, -0.5, -0.5), float3(-0.5, -0.5, -0.5),
        };

        static const float3 CubeNormals[6] =
        {
            float3( 1,  0,  0),
            float3(-1,  0,  0),
            float3( 0,  1,  0),
            float3( 0, -1,  0),
            float3( 0,  0,  1),
            float3( 0,  0, -1),
        };

        [shader("vertex")]
        public V2F mainVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
        {
            PropsRoot root = gpu_load_root<PropsRoot>();
            PropInstance inst = gpu_load<PropInstance>(root.instances, instanceId);
            float3 localPos = CubePositions[vertexId];
            float3 localNrm = CubeNormals[vertexId / 6u];

            float4 worldPos = mul(float4(localPos, 1.0), inst.worldMatrix);
            float3 worldNrm = mul(float4(localNrm, 0.0), inst.worldMatrix).xyz;

            V2F o;
            o.position = mul(worldPos, root.viewProjection);
            o.normal = worldNrm;
            o.color = inst.color;
            return o;
        }

        [shader("pixel")]
        public float4 mainFS(V2F input) : SV_TARGET
        {
            float3 n = normalize(input.normal);
            float3 lightDir = normalize(float3(0.45, -0.35, 0.82));
            float ndl = max(dot(n, lightDir), 0.0);
            float sky = 0.5 + 0.5 * n.z;
            float3 lit = input.color.rgb * (0.28 + 0.22 * sky + 0.68 * ndl);
            return float4(lit, input.color.a);
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct PropInstanceData
    {
        public Matrix4x4 WorldMatrix;
        public Vector4 Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropsRootData
    {
        public Matrix4x4 ViewProjection;
        public GPUBufferView Instances;
    }

    private readonly RenderGraph _renderGraph;
    private readonly RenderContext _renderContext;
    private readonly PropsRenderNode _propsNode;
    private readonly CameraPerspective _camera;
    private readonly GpuParticleSystem3D _particles;
    private readonly Dictionary<string, ParticleEffect3D> _effects = new();
    private readonly List<string> _effectNames = [];
    private readonly List<InstanceEntry> _instances = [];
    private FastRandom _random = new(54321);

    private InstanceEntry? _selected;
    private int _nextInstanceId = 1;
    private GizmoOperation _gizmoOperation = GizmoOperation.Translate;

    private readonly Material _propsMaterial;
    private readonly Alco.Graphics.Buffer _propsBuffer;
    private readonly int _propCount;

    private float _yaw = 0.35f;
    private float _pitch = 0.42f;
    private readonly float _distance = 20f;
    private readonly Vector3 _lookAt = new(1f, 0f, 2f);
    private const float RawMouseCountScale = 5f;

    private bool _autoSpawn = true;
    private float _autoSpawnInterval = 1.0f;
    private float _autoSpawnTimer;

    private readonly string? _screenshotPath;
    private readonly int _screenshotFrames;
    private int _frameCount;
    private SwapchainCaptureSystem? _swapchainCapture;
    private Task<RenderCaptureResult>? _screenshotRequest;
    private bool _screenshotSaved;

    private readonly int _particleTarget;
    private readonly float _particleSize;
    private float _stressLogTimer;
    private double _statFrameMs;
    private float _statFrameMsMax;
    private int _statFrames;
    private long _statAllocatedBytes;

    /// <summary>Initializes the 3D particle sandbox and its render graph.</summary>
    public Game(GameEngineSetting setting, string[] args) : base(setting)
    {
        _screenshotPath = GetArgValue(args, "--screenshot=");
        _screenshotFrames = int.TryParse(GetArgValue(args, "--frames="), out int frames) ? frames : 90;
        if (_screenshotPath != null)
            _swapchainCapture = SwapchainCapture;

        AddSystem(new ImGUISystem(this));

        _camera = new CameraPerspective
        {
            FieldOfView = 0.9f,
            AspectRatio = MainView.Size.Y > 0 ? (float)MainView.Size.X / MainView.Size.Y : 16f / 9f,
            Near = 0.1f,
            Far = 300f,
            ReverseInfiniteDepth = false,
        };

        _particles = new GpuParticleSystem3D(RenderingSystem, ShaderSystem, particleCapacity: 1 << 18, emitterSlots: 512)
        {
            DepthStencilState = DepthStencilState.Read,
        };

        Shader propsShader = ShaderSystem.GetShaderFromSource(
            "SandboxPropsCube3D", "SandboxPropsCube3D.slang", PropsShaderSource, ShaderKind.Graphics);
        _propsMaterial = propsShader.CreateMaterial("SandboxProps");
        _propsMaterial.Blend = BlendState.Opaque;
        _propsMaterial.Rasterization = RasterizationState.CullNone;

        Transform3D ground = Transform3D.Identity;
        ground.Position = new Vector3(0, 0, -0.5f);
        ground.Scale = new Vector3(80, 80, 1);
        Transform3D pillarA = Transform3D.Identity;
        pillarA.Position = new Vector3(0, 0, 3);
        pillarA.Scale = new Vector3(1.6f, 1.6f, 6);
        Transform3D pillarB = Transform3D.Identity;
        pillarB.Position = new Vector3(8, -5, 2);
        pillarB.Scale = new Vector3(2.4f, 2.4f, 4);

        PropInstanceData[] props =
        [
            new() { WorldMatrix = ground.Matrix, Color = new Vector4(0.16f, 0.17f, 0.20f, 1f) },
            new() { WorldMatrix = pillarA.Matrix, Color = new Vector4(0.35f, 0.30f, 0.25f, 1f) },
            new() { WorldMatrix = pillarB.Matrix, Color = new Vector4(0.25f, 0.30f, 0.35f, 1f) },
        ];
        _propCount = props.Length;
        _propsBuffer = RenderingSystem.CreateBuffer((ulong)(_propCount * sizeof(PropInstanceData)));
        _propsBuffer.SetData<PropInstanceData>(props);

        _propsNode = new PropsRenderNode(this);
        _renderGraph = new RenderGraph(RenderingSystem, new Extent2D(MainView.Size.X, MainView.Size.Y), "gpu_particles_3d");
        _renderGraph.Use(new ParticleSimulateNode3D(_particles));
        _renderGraph.Use(_propsNode);
        _renderGraph.Use(new ParticleRenderNode3D(
            _particles,
            _camera,
            colorLoadOp: LoadOp.Load,
            depthHandleProvider: () => _propsNode.DepthTarget));
        _renderContext = new RenderContext(RenderingSystem, frameDataByteCount: 512 * 1024);

        _effects["Flare"] = AssetSystem.Load<ParticleEffect3D>("Effects/Flare3D.afx");
        _effects["Explosion"] = AssetSystem.Load<ParticleEffect3D>("Effects/Explosion3D.afx");
        _effects["Vortex"] = AssetSystem.Load<ParticleEffect3D>("Effects/Vortex3D.afx");
        _effectNames.AddRange(_effects.Keys);

        _selected = Spawn("Flare", new Vector3(-6, -3, 0), 201);
        Spawn("Vortex", new Vector3(5, 3, 3), 202);

        _particleTarget = int.TryParse(GetArgValue(args, "--particles="), out int particleTarget) ? particleTarget : 0;
        _particleSize = float.TryParse(GetArgValue(args, "--particlesize="), out float particleSize) ? particleSize : 0.5f;
        if (_particleTarget > 0)
        {
            _effects["Fill"] = CreateFillEffect(_particleSize);
            SpawnFill(_particleTarget);
            _autoSpawn = false;
        }
    }

    private ParticleEffect3D CreateFillEffect(float size)
        => new()
        {
            Name = "Fill",
            Groups =
            [
                new ParticleGroup3D
                {
                    Name = "Fill",
                    MaxParticles = 8192,
                    Looping = true,
                    EmissionRate = 4096,
                    Lifetime = new ParticleRange(1.95f, 2.05f),
                    Shape = new ParticleShape3D { Type = ParticleShape3DType.Sphere, Radius = 0.4f },
                    Speed = new ParticleRange(0.5f, 1.5f),
                    Size = new ParticleRange(size),
                    StartColor = new ParticleColorRange(new ColorFloat(1f, 0.7f, 0.3f, 1f)),
                    EndColor = new ColorFloat(0.4f, 0.1f, 0.6f, 0f),
                    FadeIn = 0.05f,
                    FadeOut = 0.5f,
                    Texture = AssetSystem.Load<Image>("Glow"),
                    Blend = BlendState.Additive,
                },
            ],
        };

    private void SpawnFill(int target)
    {
        const int groupSize = 8192;
        int count = (target + groupSize - 1) / groupSize;
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        for (int i = 0; i < count; i++)
        {
            float x = _lookAt.X + (i % columns - (columns - 1) * 0.5f) * 2.2f;
            float y = (i / columns - (columns - 1) * 0.5f) * 1.6f;
            float z = _lookAt.Z + (i % 3 - 1) * 1.5f;
            Spawn("Fill", new Vector3(x, y, z), 2000 + i);
        }
    }

    /// <summary>Registers the particle effect asset loader (<c>.afx</c>) on top of the defaults.</summary>
    public override IEnumerable<IAssetLoader> CreateDefaultAssetLoaders()
    {
        foreach (IAssetLoader loader in base.CreateDefaultAssetLoaders())
            yield return loader;
        yield return new AssetLoaderParticleEffect(AssetSystem, ShaderSystem);
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

    private InstanceEntry SpawnVisible(string effectName)
    {
        Vector3 position = _lookAt + new Vector3(
            _random.NextFloat(-1.5f, 1.5f),
            _random.NextFloat(-1.5f, 1.5f),
            _random.NextFloat(-0.5f, 1.5f));
        return Spawn(effectName, position, (int)_random.NextUint());
    }

    private InstanceEntry SpawnRandom(string effectName)
        => Spawn(effectName, new Vector3(
            _random.NextFloat(-4f, 12f),
            _random.NextFloat(-7f, 7f),
            _random.NextFloat(0f, 5f)), (int)_random.NextUint());

    /// <inheritdoc />
    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
            Stop();

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

        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            if (!_instances[i].Instance.IsActive)
            {
                if (_instances[i] == _selected)
                    _selected = null;
                _instances[i].Instance.Dispose();
                _instances.RemoveAt(i);
            }
        }

        DrawImGuiPanel();
        DrawSelectionGizmo();

        DebugStats.Text(FrameRate);

        if (_particleTarget > 0)
        {
            float frameMs = delta * 1000f;
            _statFrameMs += frameMs;
            _statFrameMsMax = Math.Max(_statFrameMsMax, frameMs);
            _statFrames++;
            _stressLogTimer += delta;
            if (_stressLogTimer >= 1.0f)
            {
                _stressLogTimer = 0;
                long live = 0;
                long pooled = 0;
                int groups = 0;
                foreach (InstanceEntry entry in _instances)
                {
                    foreach (ParticleGroup3D group in entry.Instance.Asset.Groups)
                    {
                        groups++;
                        long capacity = Math.Max(64, (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(group.MaxParticles, 1)));
                        pooled += capacity;
                        if (group.Looping || group.Duration <= 0f)
                            live += Math.Min(capacity, (long)(group.EmissionRate * (group.Lifetime.Min + group.Lifetime.Max) * 0.5f));
                    }
                }
                long allocatedNow = GC.GetTotalAllocatedBytes(false);
                long allocatedDelta = allocatedNow - _statAllocatedBytes;
                _statAllocatedBytes = allocatedNow;
                double avgMs = _statFrameMs / Math.Max(_statFrames, 1);
                Console.WriteLine(
                    $"[stress] fps={FrameRate} avgMs={avgMs:F2} maxMs={_statFrameMsMax:F1} " +
                    $"instances={_instances.Count} groups={groups} live~={live} pooled={pooled} " +
                    $"poolCap={_particles.PoolParticleCapacity} slots={_particles.PoolEmitterSlotCapacity} allocKBs={allocatedDelta / 1024}");
                _statFrameMs = 0;
                _statFrameMsMax = 0;
                _statFrames = 0;
            }
        }

        _frameCount++;
        if (_screenshotPath != null && _screenshotRequest == null && !_screenshotSaved && _frameCount >= _screenshotFrames)
            _screenshotRequest = _swapchainCapture!.RequestCaptureAsync();

        if (MainPresenter.HasValidFrame)
        {
            SwapchainFrame frame = MainPresenter.CurrentFrame;
            if (frame.Extent.Y > 0)
                _camera.AspectRatio = (float)frame.Extent.X / frame.Extent.Y;
            _renderGraph.Resize(frame.Extent);

            bool begun = false;
            try
            {
                _renderContext.BeginFrame();
                begun = true;
                _renderGraph.Execute(
                    _renderContext,
                    RenderGraphTarget.FromSwapchain(frame, MainPresenter.SurfaceFormat),
                    delta,
                    outputFinalUsage: RGUsage.ColorAttachment);
                MainPresenter.EnqueueFrameContext(_renderContext);
            }
            catch
            {
                if (begun)
                    _renderContext.DiscardFrame();
                throw;
            }
        }

        if (_screenshotRequest is { IsCompletedSuccessfully: true })
        {
            RenderCaptureResult result = _screenshotRequest.Result;
            if (result.Success && result.PngBytes != null)
            {
                string? dir = Path.GetDirectoryName(_screenshotPath!);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
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
        bool rotating = MainView.IsFocused && Input.IsMousePressing(Mouse.Middle) && !Gizmo.IsOver;
        Input.IsMouseRelativeMode = rotating;
        if (rotating)
        {
            Vector2 mouseDelta = Input.MouseDelta / RawMouseCountScale;
            _yaw += mouseDelta.X * 0.008f;
            _pitch = Math.Clamp(_pitch + mouseDelta.Y * 0.008f, -1.4f, 1.4f);
        }

        Vector3 offset = new(
            -MathF.Cos(_pitch) * MathF.Cos(_yaw),
            -MathF.Cos(_pitch) * MathF.Sin(_yaw),
            MathF.Sin(_pitch));
        Vector3 position = _lookAt + offset * _distance;
        _camera.Transform = new Transform3D(position, LookRotation(_lookAt - position, Vector3.UnitZ));
    }

    private void DrawImGuiPanel()
    {
        ImGui.SetNextWindowSize(new Vector2(680, 480), ImGuiCond.Appearing);
        ImGui.Begin("GPU Particles 3D");

        int groups = 0;
        foreach (InstanceEntry entry in _instances)
            groups += entry.Instance.Asset.Groups.Count;
        ImGui.Text($"Instances: {_instances.Count}   Emitter groups: {groups}");

        const float inspectorWidth = 340f;
        if (ImGui.BeginChild("##instances", new Vector2(-inspectorWidth, -1)))
        {
            ImGui.SeparatorText("Instances");
            foreach (InstanceEntry entry in _instances)
            {
                string state = entry.Instance.IsPlaying ? "playing" : "fading";
                if (ImGui.Selectable($"{entry.Instance.Asset.Name} #{entry.Id} ({state})", entry == _selected))
                    _selected = entry;
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
                    entry.Instance.Dispose();
                _instances.Clear();
                _selected = null;
            }

            ImGui.SeparatorText("Spawn");
            foreach (string effectName in _effectNames)
            {
                if (ImGui.Button(effectName))
                    _selected = SpawnVisible(effectName);
            }

            ImGui.SeparatorText("Auto Spawn");
            ImGui.Checkbox("Auto Spawn Explosions", ref _autoSpawn);
            ImGui.SliderFloat("Spawn Interval", ref _autoSpawnInterval, 0.05f, 3.0f);
        }
        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("##selected", new Vector2(0, -1)))
        {
            if (_selected != null)
            {
                ParticleEffectInstance3D instance = _selected.Instance;
                ImGui.SeparatorText($"Selected: {instance.Asset.Name} #{_selected.Id}");

                ImGui.Text("Gizmo:");
                ImGui.SameLine();
                if (ImGui.RadioButton("Translate", _gizmoOperation == GizmoOperation.Translate))
                    _gizmoOperation = GizmoOperation.Translate;
                ImGui.SameLine();
                if (ImGui.RadioButton("Rotate", _gizmoOperation == GizmoOperation.Rotate))
                    _gizmoOperation = GizmoOperation.Rotate;

                Transform3D transform = instance.Transform;
                if (ImGui.EditTransform3D(ref transform))
                    instance.Transform = transform;

                for (int i = 0; i < instance.GroupCount; i++)
                {
                    if (ImGui.CollapsingHeader($"{instance.GetGroupName(i)}##group{i}", ImGuiTreeNodeFlags.DefaultOpen))
                        DrawGroupEditor(instance, i);
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

    private void DrawSelectionGizmo()
    {
        if (_selected == null)
            return;
        Transform3D transform = _selected.Instance.Transform;
        if (Gizmo.Manipulate(
            _camera.ViewMatrix,
            _camera.ProjectionMatrix,
            _gizmoOperation,
            GizmoMode.Local,
            ref transform))
        {
            _selected.Instance.Transform = transform;
        }
    }

    private static void DrawGroupEditor(ParticleEffectInstance3D instance, int groupIndex)
    {
        ImGui.PushID(groupIndex);
        EmitterParams3D parameters = instance.GetGroupParams(groupIndex);
        bool edited = false;

        float rate = instance.GetGroupEmissionRate(groupIndex);
        if (ImGui.DragFloat("Emission Rate", ref rate, 1f, 0f, 5000f))
            instance.SetGroupEmissionRate(groupIndex, rate);

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
            instance.SetGroupParams(groupIndex, parameters);
        ImGui.PopID();
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        GraphicsDevice.WaitIdle();
        _particles.Dispose();
        _propsBuffer.Destroy();
        _renderGraph.Destroy();
        _renderContext.Destroy();
    }

    private static string? GetArgValue(string[] args, string prefix)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
                return arg[prefix.Length..];
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

    private sealed class PropsRenderNode(Game game) : RenderGraphNode("PropsScene3D")
    {
        private RGTexture _colorTarget;
        public RGTexture DepthTarget { get; private set; }

        protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
        {
            _colorTarget = builder.Output;
            DepthTarget = builder.CreateTexture(new RGTextureDesc
            {
                Name = "SceneDepth",
                Format = Format.D32Float,
                Usage = TextureUsage.DepthStencilAttachment,
            });
            pass.Write(_colorTarget, RGUsage.ColorAttachmentWrite);
            pass.Write(DepthTarget, RGUsage.DepthStencil);
        }

        protected override void ExecutePass(in RenderGraphContext context)
        {
            RenderContext rc = context.RenderContext;
            var desc = new RenderingDesc
            {
                Colors =
                [
                    new ColorAttachment
                    {
                        RenderView = context.GetRenderView(_colorTarget),
                        Load = LoadOp.Clear,
                        Store = StoreOp.Store,
                        Clear = new Vector4(0.06f, 0.07f, 0.09f, 1f),
                    },
                ],
                Depth = new DepthAttachment
                {
                    RenderView = context.GetRenderView(DepthTarget),
                    Load = LoadOp.Clear,
                    Store = StoreOp.Store,
                    Clear = 1.0f,
                },
            };
            Extent2D vp = context.Viewport;

            GPUAllocation<byte> alloc = rc.FrameData.Allocate((ulong)sizeof(PropsRootData));
            var root = new PropsRootData
            {
                ViewProjection = game._camera.ViewProjectionMatrix,
                Instances = game._propsBuffer.View,
            };
            MemoryMarshal.Write(alloc.CpuSpan, in root);

            using RenderContext.RenderScope render = rc.BeginRenderPass(desc);
            render.SetViewport(new Viewport { Width = vp.X, Height = vp.Y });
            render.SetScissor(new Scissor { Width = vp.X, Height = vp.Y });
            render.Draw(
                game._propsMaterial,
                alloc.GPURange,
                DepthStencilState.Write,
                vertexCount: 36,
                instanceCount: (uint)game._propCount);
        }
    }

    private sealed class InstanceEntry(ParticleEffectInstance3D instance, int id)
    {
        public readonly ParticleEffectInstance3D Instance = instance;
        public readonly int Id = id;
    }
}
