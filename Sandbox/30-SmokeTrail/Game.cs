using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco;
using Alco.Effects;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.Rendering;

/// <summary>Top-down twin-stick arena shooter demonstrating the forward 3D arena renderer and 3D GPU smoke trail ribbons.</summary>
/// <remarks>Controls: WASD move, mouse aim, left mouse fires, ESC exits. Pass <c>--screenshot=&lt;path.png&gt; [--frames=N]</c> to run a scripted demo and capture a frame.</remarks>
public unsafe class Game : GameEngine
{
    private const string ArenaBoxShaderSource = """
        #language slang 2025
        module SandboxArenaBoxes3D;
        import Core;

        public struct ArenaBoxInstance
        {
            public float4x4 worldMatrix;
            public float4 baseColor;
            public float4 emissiveChecker; // xyz = emissive, w > 0.5 enables ground checker
        };

        [GPURoot]
        public struct ArenaRoot
        {
            public float4x4 viewProjection;
            public float4 sunDirIntensity; // xyz = direction TO sun, w = sun scale
            public float4 sunColor;
            public float4 skyHorizon;
            public float4 skyZenith;
            public GPUBufferView instances;
        };

        public struct V2F
        {
            public float4 position : SV_POSITION;
            public float3 worldPos : TEXCOORD0;
            public float3 normal : TEXCOORD1;
            public float4 baseColor : COLOR0;
            public nointerpolation float4 emissiveChecker : TEXCOORD2;
        };

        static const float3 CubePositions[36] =
        {
            float3( 0.5, -0.5, -0.5), float3( 0.5,  0.5, -0.5), float3( 0.5,  0.5,  0.5),
            float3( 0.5, -0.5, -0.5), float3( 0.5,  0.5,  0.5), float3( 0.5, -0.5,  0.5),
            float3(-0.5,  0.5, -0.5), float3(-0.5, -0.5, -0.5), float3(-0.5, -0.5,  0.5),
            float3(-0.5,  0.5, -0.5), float3(-0.5, -0.5,  0.5), float3(-0.5,  0.5,  0.5),
            float3( 0.5,  0.5, -0.5), float3(-0.5,  0.5, -0.5), float3(-0.5,  0.5,  0.5),
            float3( 0.5,  0.5, -0.5), float3(-0.5,  0.5,  0.5), float3( 0.5,  0.5,  0.5),
            float3(-0.5, -0.5, -0.5), float3( 0.5, -0.5, -0.5), float3( 0.5, -0.5,  0.5),
            float3(-0.5, -0.5, -0.5), float3( 0.5, -0.5,  0.5), float3(-0.5, -0.5,  0.5),
            float3(-0.5, -0.5,  0.5), float3( 0.5, -0.5,  0.5), float3( 0.5,  0.5,  0.5),
            float3(-0.5, -0.5,  0.5), float3( 0.5,  0.5,  0.5), float3(-0.5,  0.5,  0.5),
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
            ArenaRoot root = gpu_load_root<ArenaRoot>();
            ArenaBoxInstance inst = gpu_load<ArenaBoxInstance>(root.instances, instanceId);

            float3 localPos = CubePositions[vertexId];
            float3 localNrm = CubeNormals[vertexId / 6u];

            float4 worldPos = mul(float4(localPos, 1.0), inst.worldMatrix);
            float3 worldNrm = mul(float4(localNrm, 0.0), inst.worldMatrix).xyz;

            V2F o;
            o.position = mul(worldPos, root.viewProjection);
            o.worldPos = worldPos.xyz;
            o.normal = worldNrm;
            o.baseColor = inst.baseColor;
            o.emissiveChecker = inst.emissiveChecker;
            return o;
        }

        [shader("pixel")]
        public float4 mainFS(V2F input) : SV_TARGET
        {
            ArenaRoot root = gpu_load_root<ArenaRoot>();
            float3 n = normalize(input.normal);
            float3 albedo = input.baseColor.rgb;
            if (input.emissiveChecker.w > 0.5)
            {
                int cx = int(floor(input.worldPos.x * 0.5));
                int cy = int(floor(input.worldPos.y * 0.5));
                float checker = ((cx + cy) & 1) == 0 ? 1.05 : 0.85;
                albedo *= checker;
            }

            float ndl = max(dot(n, root.sunDirIntensity.xyz), 0.0);
            float skyT = clamp(n.z * 0.5 + 0.5, 0.0, 1.0);
            float3 ambient = lerp(root.skyHorizon.rgb, root.skyZenith.rgb, skyT) * 0.45;
            float3 hdr = albedo * (ambient + root.sunColor.rgb * (ndl * root.sunDirIntensity.w))
                       + input.emissiveChecker.xyz;

            // Filmic tone curve + sRGB gamma for swapchain presentation.
            float3 mapped = hdr / (hdr + float3(1.0, 1.0, 1.0));
            return float4(pow(max(mapped, float3(0.0)), float3(1.0 / 2.2)), 1.0);
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct ArenaBoxInstanceData
    {
        public Matrix4x4 WorldMatrix;
        public Vector4 BaseColor;
        public Vector4 EmissiveChecker;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ArenaRootData
    {
        public Matrix4x4 ViewProjection;
        public Vector4 SunDirIntensity;
        public Vector4 SunColor;
        public Vector4 SkyHorizon;
        public Vector4 SkyZenith;
        public GPUBufferView Instances;
    }

    private sealed class SceneBox
    {
        public Transform3D Transform = Transform3D.Identity;
        public Vector3 BaseColor;
        public Vector3 Emissive;
        public bool IsCheckerGround;
    }

    private const float CameraNear = 0.1f;
    private const float CameraFov = 0.78f;
    private static readonly Vector3 CameraOffset = new(0f, 12.5f, 19f);

    private readonly RenderGraph _renderGraph;
    private readonly RenderContext _renderContext;
    private readonly ArenaSceneNode _arenaNode;
    private readonly CameraPerspective _camera;
    private readonly Material _arenaMaterial;
    private readonly Alco.Graphics.Buffer[] _boxBuffers;
    private readonly ArenaBoxInstanceData[] _boxStaging;
    private const int MaxArenaBoxes = 512;
    private int _frameParity;

    private readonly GpuTrailSystem3D _trails;
    private readonly TrailEffect3D _bulletTrailEffect;
    private readonly TrailEffect3D _puffTrailEffect;

    private readonly List<SceneBox> _sceneBoxes = [];

    private readonly record struct Box(Vector3 Min, Vector3 Max)
    {
        public Vector3 Center => (Min + Max) * 0.5f;
        public Vector3 Size => Max - Min;
    }

    private readonly List<Box> _colliders = [];
    private readonly Vector3[] _lampPositions = new Vector3[4];

    private const float ArenaHalf = 21f;
    private const float BoundsHalf = 20.2f;

    private readonly SceneBox _playerBody;
    private readonly SceneBox _playerVisor;
    private Vector3 _playerPosition = new(0f, 0f, PlayerZ);
    private float _playerYaw;
    private float _playerHp = MaxHp;
    private float _invulnerabilityTimer;
    private Vector3 _cameraFocus = new(0f, 0f, PlayerZ);
    private Vector2 _aimDirection = new(1f, 0f);

    private const float PlayerZ = 0.55f;
    private const float PlayerRadius = 0.5f;
    private const float MoveSpeed = 9.5f;
    private const float MaxHp = 100f;

    private sealed class Bullet
    {
        public required Vector3 Position;
        public Vector3 PreviousPosition;
        public required Vector3 Direction;
        public required SceneBox Body;
        public TrailEffectInstance3D? Trail;
        public float Age;
    }

    private readonly List<Bullet> _bullets = [];
    private float _fireCooldown;
    private float _muzzleFlash;
    private Vector3 _lastMuzzle;
    private const float FireInterval = 0.12f;
    private const float BulletSpeed = 48f;
    private const float BulletMaxAge = 1.4f;

    private sealed class Enemy
    {
        public required Vector3 Position;
        public Vector3 PreviousPosition;
        public required SceneBox Body;
        public float Speed;
        public float Phase;
    }

    private readonly List<Enemy> _enemies = [];
    private int _wave;
    private float _wavePauseTimer = 1.2f;
    private float _waveBannerTimer;
    private int _score;
    private int _bestScore;
    private int _shots;
    private int _hits;
    private const float EnemyRadius = 0.6f;
    private const float ContactDamage = 15f;

    private sealed class Puff
    {
        public required TrailEffectInstance3D Trail;
        public Vector3 Position;
        public float EmitTimer;
    }

    private readonly List<Puff> _puffs = [];
    private FastRandom _random = new(987654);
    private float _time;

    private readonly string? _screenshotPath;
    private readonly int _screenshotFrames;
    private SwapchainCaptureSystem? _swapchainCapture;
    private Task<RenderCaptureResult>? _screenshotRequest;
    private bool _screenshotSaved;

    private int _tickCount;
    private long _lastTickTimestamp;

    private float RenderAlpha
    {
        get
        {
            double elapsed = (Stopwatch.GetTimestamp() - _lastTickTimestamp) / (double)Stopwatch.Frequency;
            return Math.Clamp((float)(elapsed * 60.0), 0f, 1f);
        }
    }

    /// <summary>Initializes the arena, rendering resources, and optional screenshot mode.</summary>
    public Game(GameEngineSetting setting, string[] args) : base(setting)
    {
        _screenshotPath = GetArgValue(args, "--screenshot=");
        _screenshotFrames = int.TryParse(GetArgValue(args, "--frames="), out int frames) ? frames : 150;
        if (_screenshotPath != null)
        {
            _swapchainCapture = SwapchainCapture;
            Input.IsCursorVisible = false;
        }

        AddSystem(new ImGUISystem(this));

        _camera = new CameraPerspective
        {
            FieldOfView = CameraFov,
            AspectRatio = MainView.Size.Y > 0 ? (float)MainView.Size.X / MainView.Size.Y : 16f / 9f,
            Near = CameraNear,
            Far = 4096f,
            ReverseInfiniteDepth = true,
        };

        Shader arenaShader = ShaderSystem.GetShaderFromSource(
            "SandboxArenaBoxes3D", "SandboxArenaBoxes3D.slang", ArenaBoxShaderSource, ShaderKind.Graphics);
        _arenaMaterial = arenaShader.CreateMaterial("ArenaBoxes");
        _arenaMaterial.Blend = BlendState.Opaque;
        _arenaMaterial.Rasterization = RasterizationState.CullNone;

        ulong bufBytes = (ulong)(MaxArenaBoxes * sizeof(ArenaBoxInstanceData));
        _boxBuffers = [RenderingSystem.CreateBuffer(bufBytes), RenderingSystem.CreateBuffer(bufBytes)];
        _boxStaging = new ArenaBoxInstanceData[MaxArenaBoxes];

        _trails = new GpuTrailSystem3D(RenderingSystem, ShaderSystem, pointCapacity: 1 << 17, trailSlots: 256)
        {
            DepthStencilState = DepthStencilState.ReadReverseZ,
        };
        _bulletTrailEffect = new TrailEffect3D
        {
            Life = 1.8f,
            Spacing = 0.35f,
            Width0 = 0.18f,
            Width1 = 0.18f,
            Opacity = 1f,
            FadeIn = 0.04f,
            FadeOut = 0.55f,
            ExpectedPoints = 512,
            Depth = DepthStencilState.ReadReverseZ,
            Material = new MaterialAsset
            {
                Name = "bullet-smoke",
                Surface = "TrailSurfaceSmoke",
                Parameters = new Dictionary<string, ShaderValue>
                {
                    ["smokeColor"] = new Vector4(0.45f, 0.48f, 0.55f, 0.65f),
                    ["shape"] = new Vector4(0.35f, 0.35f, 0.06f, 0.45f),
                    ["width"] = new Vector4(0.12f, 0.5f, 0f, 0f),
                    ["edge"] = new Vector4(0.05f, 1.4f, 0.6f, 0f),
                },
            },
        };
        _puffTrailEffect = new TrailEffect3D
        {
            Life = 1.1f,
            Spacing = 0.05f,
            Width0 = 0.5f,
            Width1 = 1.4f,
            Opacity = 1f,
            FadeIn = 0.06f,
            FadeOut = 0.7f,
            ExpectedPoints = 64,
            Depth = DepthStencilState.ReadReverseZ,
            Material = new MaterialAsset
            {
                Name = "blast-smoke",
                Surface = "TrailSurfaceSmoke",
                Parameters = new Dictionary<string, ShaderValue>
                {
                    ["smokeColor"] = new Vector4(0.4f, 0.3f, 0.22f, 0.7f),
                    ["shape"] = new Vector4(0.4f, 0.4f, 0.6f, 0.4f),
                    ["width"] = new Vector4(0.5f, 0.5f, 0f, 0f),
                    ["edge"] = new Vector4(0.05f, 1.2f, 0.5f, 0f),
                    ["motion"] = new Vector4(0.7f, 0.6f, 0f, 0f),
                },
            },
        };

        _arenaNode = new ArenaSceneNode(this);
        _renderGraph = new RenderGraph(RenderingSystem, new Extent2D(MainView.Size.X, MainView.Size.Y), "smoke_trail_arena");
        _renderGraph.Use(_arenaNode);
        _renderGraph.Use(new TrailRenderNode3D(
            _trails,
            _camera,
            colorLoadOp: LoadOp.Load,
            depthHandleProvider: () => _arenaNode.DepthTarget));
        _renderContext = new RenderContext(RenderingSystem, frameDataByteCount: 512 * 1024);

        BuildWorld();

        _playerBody = new SceneBox
        {
            BaseColor = new Vector3(0.3f, 0.55f, 0.85f),
            Transform = new Transform3D(_playerPosition, Quaternion.Identity, new Vector3(1.15f, 0.72f, 0.38f)),
        };
        _playerVisor = new SceneBox
        {
            BaseColor = new Vector3(0.05f, 0.1f, 0.12f),
            Emissive = new Vector3(0.5f, 3.2f, 4.0f),
        };
        _sceneBoxes.Add(_playerBody);
        _sceneBoxes.Add(_playerVisor);
    }

    private void BuildWorld()
    {
        _sceneBoxes.Add(new SceneBox
        {
            BaseColor = new Vector3(0.16f, 0.17f, 0.20f),
            Transform = new Transform3D(new Vector3(0f, 0f, -0.5f), Quaternion.Identity, new Vector3(120f, 120f, 1f)),
            IsCheckerGround = true,
        });

        (Vector3 Center, Vector3 Size)[] walls =
        [
            (new Vector3(ArenaHalf + 0.5f, 0, 1.2f), new Vector3(1, 44.5f, 2.4f)),
            (new Vector3(-ArenaHalf - 0.5f, 0, 1.2f), new Vector3(1, 44.5f, 2.4f)),
            (new Vector3(0, ArenaHalf + 0.5f, 1.2f), new Vector3(44.5f, 1, 2.4f)),
            (new Vector3(0, -ArenaHalf - 0.5f, 1.2f), new Vector3(44.5f, 1, 2.4f)),
        ];
        foreach ((Vector3 center, Vector3 size) in walls)
            AddStaticBox(center, size, new Vector3(0.26f, 0.27f, 0.30f), collides: true);

        (Vector3 Center, Vector3 Size)[] trims =
        [
            (new Vector3(ArenaHalf, 0, 2.46f), new Vector3(0.2f, 43f, 0.12f)),
            (new Vector3(-ArenaHalf, 0, 2.46f), new Vector3(0.2f, 43f, 0.12f)),
            (new Vector3(0, ArenaHalf, 2.46f), new Vector3(43f, 0.2f, 0.12f)),
            (new Vector3(0, -ArenaHalf, 2.46f), new Vector3(43f, 0.2f, 0.12f)),
        ];
        foreach ((Vector3 center, Vector3 size) in trims)
            AddStaticBox(center, size, new Vector3(0.3f, 0.15f, 0.05f), collides: false, emissive: new Vector3(1.8f, 0.85f, 0.28f));

        (Vector3 Center, Vector3 Size, int Kind)[] crates =
        [
            (new Vector3(-8, -4, 0.9f), new Vector3(2.5f, 2.5f, 1.8f), 0),
            (new Vector3(6, -7, 0.6f), new Vector3(3, 1.6f, 1.2f), 1),
            (new Vector3(10, 4, 1.3f), new Vector3(2, 2, 2.6f), 2),
            (new Vector3(-5, 7, 0.7f), new Vector3(1.8f, 3.2f, 1.4f), 1),
            (new Vector3(0, -12, 0.8f), new Vector3(4, 1.8f, 1.6f), 0),
            (new Vector3(-13, 3, 1.1f), new Vector3(2.2f, 2.2f, 2.2f), 1),
            (new Vector3(14, -3, 0.7f), new Vector3(2.4f, 1.8f, 1.4f), 0),
            (new Vector3(3, 13, 0.9f), new Vector3(3.2f, 2, 1.8f), 2),
            (new Vector3(-10, -12, 0.6f), new Vector3(2, 2, 1.2f), 0),
            (new Vector3(13, 12, 1.0f), new Vector3(2.6f, 2.6f, 2.0f), 1),
            (new Vector3(-16, 10, 1.2f), new Vector3(1.6f, 3.6f, 2.4f), 2),
            (new Vector3(17, -12, 0.9f), new Vector3(1.8f, 1.8f, 1.8f), 0),
        ];
        Vector3[] kindColors = [new(0.4f, 0.26f, 0.14f), new(0.3f, 0.34f, 0.42f), new(0.32f, 0.36f, 0.22f)];
        foreach ((Vector3 center, Vector3 size, int kind) in crates)
            AddStaticBox(center, size, kindColors[kind], collides: true);

        Vector2[] corners = [new(17, 17), new(-17, 17), new(17, -17), new(-17, -17)];
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 c = corners[i];
            AddStaticBox(new Vector3(c, 1.7f), new Vector3(1.7f, 1.7f, 3.4f), new Vector3(0.2f, 0.21f, 0.25f), collides: true);
            Vector3 lamp = new(c.X, c.Y, 3.7f);
            AddStaticBox(lamp, new Vector3(1.0f, 1.0f, 0.55f), new Vector3(0.06f, 0.1f, 0.12f),
                collides: false, emissive: new Vector3(0.35f, 2.2f, 2.8f));
            _lampPositions[i] = lamp + new Vector3(0, 0, 0.6f);
        }
    }

    private void AddStaticBox(Vector3 center, Vector3 size, Vector3 color, bool collides, Vector3 emissive = default)
    {
        _sceneBoxes.Add(new SceneBox
        {
            BaseColor = color,
            Emissive = emissive,
            Transform = new Transform3D(center, Quaternion.Identity, size),
        });
        if (collides)
        {
            Vector3 half = size * 0.5f;
            _colliders.Add(new Box(center - half, center + half));
        }
    }

    /// <inheritdoc />
    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
            Stop();
        _time += delta;

        UpdatePlayer(delta);
        UpdateCamera(delta);
        ApplyRenderTransforms(RenderAlpha);

        _muzzleFlash = MathF.Max(0f, _muzzleFlash - delta / 0.07f);
        if (_waveBannerTimer > 0f)
            _waveBannerTimer -= delta;

        _trails.Update(delta);

        DrawHud();
        DebugStats.Text(FrameRate);

        if (_screenshotPath != null && _screenshotRequest == null && !_screenshotSaved && _tickCount >= _screenshotFrames)
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

    /// <inheritdoc />
    protected override void OnTick(float delta)
    {
        _lastTickTimestamp = Stopwatch.GetTimestamp();
        _tickCount++;

        if (_screenshotPath != null)
            TickScriptedDemo();
        TickShooting(delta);
        TickBullets(delta);
        TickEnemies(delta);
        TickPuffs(delta);
        TickWaves(delta);

        if (_invulnerabilityTimer > 0f)
            _invulnerabilityTimer -= delta;
    }

    private void UpdateCamera(float delta)
    {
        float blend = 1f - MathF.Exp(-8f * delta);
        _cameraFocus = Vector3.Lerp(_cameraFocus, _playerPosition, blend);
        Vector3 eye = _cameraFocus + CameraOffset;
        Vector3 forward = Vector3.Normalize(_cameraFocus - eye);
        _camera.Transform = new Transform3D(eye, LookRotation(forward, Vector3.UnitZ));
    }

    private void UpdatePlayer(float delta)
    {
        Vector3 cameraForward = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, _camera.Transform.Rotation));
        Vector2 flatForward = Vector2.Normalize(new Vector2(cameraForward.X, cameraForward.Y));
        Vector2 flatRight = new(-flatForward.Y, flatForward.X);

        Vector2 move = Vector2.Zero;
        if (_screenshotPath == null)
        {
            if (Input.IsKeyPressing(KeyCode.W)) move += flatForward;
            if (Input.IsKeyPressing(KeyCode.S)) move -= flatForward;
            if (Input.IsKeyPressing(KeyCode.D)) move += flatRight;
            if (Input.IsKeyPressing(KeyCode.A)) move -= flatRight;
        }
        if (move.LengthSquared() > 0f)
            _playerPosition += new Vector3(Vector2.Normalize(move), 0f) * MoveSpeed * delta;

        PushOutOfColliders(ref _playerPosition, PlayerRadius);
        _playerPosition.X = Math.Clamp(_playerPosition.X, -BoundsHalf, BoundsHalf);
        _playerPosition.Y = Math.Clamp(_playerPosition.Y, -BoundsHalf, BoundsHalf);
        _playerPosition.Z = PlayerZ;

        if (_screenshotPath == null)
        {
            Ray3D aimRay = ScreenPointToWorldRay(MainView.MousePosition);
            if (MathF.Abs(aimRay.Displacement.Z) > 1e-5f)
            {
                float t = (PlayerZ - aimRay.Origin.Z) / aimRay.Displacement.Z;
                if (t > 0f)
                {
                    Vector2 aimPoint = new Vector2(aimRay.Origin.X, aimRay.Origin.Y) + t * new Vector2(aimRay.Displacement.X, aimRay.Displacement.Y);
                    Vector2 toAim = aimPoint - new Vector2(_playerPosition.X, _playerPosition.Y);
                    if (toAim.LengthSquared() > 0.04f)
                        _aimDirection = Vector2.Normalize(toAim);
                }
            }
        }
        _playerYaw = MathF.Atan2(_aimDirection.Y, _aimDirection.X);

        Quaternion yawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, _playerYaw);
        _playerBody.Transform = new Transform3D(_playerPosition, yawRotation, new Vector3(1.15f, 0.72f, 0.38f));
        Vector3 visorOffset = Vector3.Transform(new Vector3(0.46f, 0f, 0.16f), yawRotation);
        _playerVisor.Transform = new Transform3D(_playerPosition + visorOffset, yawRotation, new Vector3(0.4f, 0.42f, 0.16f));
    }

    private Ray3D ScreenPointToWorldRay(Vector2 screenPoint)
    {
        Vector2 size = new(MainView.Size.X, MainView.Size.Y);
        Vector2 clip = new(2f * screenPoint.X / size.X - 1f, 1f - 2f * screenPoint.Y / size.Y);
        Matrix4x4.Invert(_camera.ViewProjectionMatrix, out Matrix4x4 inverseViewProjection);
        Vector4 point = Vector4.Transform(new Vector4(clip, 0.5f, 1f), inverseViewProjection);
        if (point.W != 0f)
            point /= point.W;
        Vector3 cameraPosition = _camera.Transform.Position;
        return new Ray3D(cameraPosition, Vector3.Normalize(new Vector3(point.X, point.Y, point.Z) - cameraPosition));
    }

    private void ApplyRenderTransforms(float alpha)
    {
        foreach (Enemy enemy in _enemies)
        {
            Vector3 position = Vector3.Lerp(enemy.PreviousPosition, enemy.Position, alpha);
            float bob = 0.6f + 0.12f * MathF.Sin(_time * 2.2f + enemy.Phase);
            enemy.Body.Transform = new Transform3D(
                new Vector3(position.X, position.Y, bob),
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, _time * 1.6f + enemy.Phase),
                new Vector3(1.0f));
            float pulse = 0.7f + 0.3f * MathF.Sin(_time * 4f + enemy.Phase);
            enemy.Body.Emissive = new Vector3(2.4f, 0.32f, 0.08f) * pulse;
        }
        foreach (Bullet bullet in _bullets)
        {
            Vector3 position = Vector3.Lerp(bullet.PreviousPosition, bullet.Position, alpha);
            bullet.Body.Transform = new Transform3D(position, bullet.Body.Transform.Rotation, bullet.Body.Transform.Scale);
        }
    }

    private void TickShooting(float delta)
    {
        _fireCooldown -= delta;

        bool wantsFire = _screenshotPath != null
            ? _enemies.Count > 0 && _tickCount >= 70
            : MainView.IsFocused && Input.IsMousePressing(Mouse.Left);
        if (!wantsFire || _fireCooldown > 0f)
            return;
        _fireCooldown = FireInterval;

        Vector3 direction = new(_aimDirection, 0f);
        Vector3 muzzle = _playerPosition + direction * 0.75f;
        muzzle.Z = PlayerZ;
        _lastMuzzle = muzzle;
        _muzzleFlash = 1f;

        var body = new SceneBox
        {
            BaseColor = new Vector3(1f, 0.6f, 0.2f),
            Emissive = new Vector3(4f, 1.8f, 0.5f),
            Transform = new Transform3D(muzzle, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, _playerYaw),
                new Vector3(0.32f, 0.15f, 0.15f)),
        };
        _sceneBoxes.Add(body);

        if (!_trails.TryCreateInstance(_bulletTrailEffect, muzzle, out TrailEffectInstance3D trail))
            trail = null!;
        _bullets.Add(new Bullet
        {
            Position = muzzle,
            PreviousPosition = muzzle,
            Direction = direction,
            Body = body,
            Trail = trail,
        });
        _shots++;
    }

    private void TickBullets(float delta)
    {
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            Bullet bullet = _bullets[i];
            Vector3 from = bullet.Position;
            Vector3 to = from + bullet.Direction * BulletSpeed * delta;
            bullet.Age += delta;

            if (TryHitWorld(from, to, out Vector3 hit, out Enemy? hitEnemy))
            {
                bullet.Trail?.Finish(hit);
                RemoveBullet(i);
                if (hitEnemy != null)
                {
                    KillEnemy(hitEnemy);
                    _hits++;
                    _score += 100;
                }
                continue;
            }
            if (bullet.Age >= BulletMaxAge)
            {
                bullet.Trail?.Finish(to);
                RemoveBullet(i);
                continue;
            }
            bullet.PreviousPosition = from;
            bullet.Position = to;
            bullet.Trail?.ExtendTo(to);
        }
    }

    private void RemoveBullet(int index)
    {
        _sceneBoxes.Remove(_bullets[index].Body);
        _bullets.RemoveAt(index);
    }

    private bool TryHitWorld(Vector3 from, Vector3 to, out Vector3 hit, out Enemy? hitEnemy)
    {
        hitEnemy = null;
        float bestT = float.MaxValue;

        foreach (Box box in _colliders)
        {
            if (RayBox(from, to, box, out float t) && t < bestT)
            {
                bestT = t;
                hitEnemy = null;
            }
        }
        foreach (Enemy enemy in _enemies)
        {
            Vector3 segment = to - from;
            float length = segment.Length();
            if (length < 1e-5f)
                continue;
            Vector3 toEnemy = enemy.Position - from;
            float t = Math.Clamp(Vector3.Dot(toEnemy, segment / length) / length, 0f, 1f);
            if (Vector3.DistanceSquared(from + segment * t, enemy.Position) < EnemyRadius * EnemyRadius && t < bestT)
            {
                bestT = t;
                hitEnemy = enemy;
            }
        }
        if (bestT == float.MaxValue)
        {
            hit = default;
            return false;
        }
        hit = from + (to - from) * bestT;
        return true;
    }

    private void TickEnemies(float delta)
    {
        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            Enemy enemy = _enemies[i];
            enemy.PreviousPosition = enemy.Position;
            Vector3 toPlayer = _playerPosition - enemy.Position;
            float distance = new Vector2(toPlayer.X, toPlayer.Y).Length();
            if (distance > 1e-4f)
                enemy.Position += toPlayer / distance * enemy.Speed * delta;

            for (int j = 0; j < _enemies.Count; j++)
            {
                if (j == i)
                    continue;
                Vector2 apart = new(enemy.Position.X - _enemies[j].Position.X, enemy.Position.Y - _enemies[j].Position.Y);
                float apartLength = apart.Length();
                if (apartLength is > 1e-4f and < 1.1f)
                    enemy.Position += new Vector3(apart / apartLength * (1.1f - apartLength) * 0.5f, 0f);
            }

            PushOutOfColliders(ref enemy.Position, EnemyRadius);
            enemy.Position.X = Math.Clamp(enemy.Position.X, -BoundsHalf, BoundsHalf);
            enemy.Position.Y = Math.Clamp(enemy.Position.Y, -BoundsHalf, BoundsHalf);

            if (distance < PlayerRadius + EnemyRadius + 0.1f)
            {
                if (_screenshotPath == null && _invulnerabilityTimer <= 0f)
                {
                    _playerHp -= ContactDamage;
                    _invulnerabilityTimer = 0.4f;
                }
                KillEnemy(enemy);
                if (_playerHp <= 0f)
                {
                    OnPlayerDeath();
                    break;
                }
                continue;
            }
        }
    }

    private void TickWaves(float delta)
    {
        if (_screenshotPath != null || _enemies.Count > 0)
            return;
        _wavePauseTimer -= delta;
        if (_wavePauseTimer > 0f)
            return;

        _wave++;
        _waveBannerTimer = 2.5f;
        int count = Math.Min(3 + 2 * _wave, 18);
        for (int i = 0; i < count; i++)
            SpawnEnemy(RandomEdgePosition(9f));
    }

    private Vector3 RandomEdgePosition(float minPlayerDistance)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float angle = _random.NextFloat(0f, MathF.PI * 2f);
            float radius = _random.NextFloat(15f, 19f);
            Vector3 position = new(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0.6f);
            position.X = Math.Clamp(position.X, -BoundsHalf, BoundsHalf);
            position.Y = Math.Clamp(position.Y, -BoundsHalf, BoundsHalf);
            if (Vector3.Distance(position, _playerPosition) >= minPlayerDistance)
                return position;
        }
        return new Vector3(BoundsHalf, BoundsHalf, 0.6f);
    }

    private void SpawnEnemy(Vector3 position)
    {
        var body = new SceneBox
        {
            BaseColor = new Vector3(0.42f, 0.08f, 0.06f),
            Emissive = new Vector3(2.4f, 0.32f, 0.08f),
            Transform = new Transform3D(position, Quaternion.Identity, new Vector3(1.0f)),
        };
        _sceneBoxes.Add(body);
        _enemies.Add(new Enemy
        {
            Position = position,
            PreviousPosition = position,
            Body = body,
            Speed = MathF.Min(3.4f + 0.15f * _wave, 6f),
            Phase = _random.NextFloat(0f, MathF.PI * 2f),
        });
    }

    private void KillEnemy(Enemy enemy)
    {
        _sceneBoxes.Remove(enemy.Body);
        _enemies.Remove(enemy);

        if (_trails.TryCreateInstance(_puffTrailEffect, enemy.Position, out TrailEffectInstance3D puff))
            _puffs.Add(new Puff { Trail = puff, Position = enemy.Position, EmitTimer = 0.18f });
    }

    private void TickPuffs(float delta)
    {
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            Puff puff = _puffs[i];
            if (puff.EmitTimer > 0f)
            {
                puff.EmitTimer -= delta;
                Vector3 jitter = new(
                    _random.NextFloat(-0.2f, 0.2f),
                    _random.NextFloat(-0.2f, 0.2f),
                    _random.NextFloat(0f, 0.25f));
                puff.Trail.ExtendTo(puff.Position + jitter);
                if (puff.EmitTimer <= 0f)
                    puff.Trail.Finish(puff.Position);
            }
            if (!puff.Trail.IsAlive)
                _puffs.RemoveAt(i);
        }
    }

    private void OnPlayerDeath()
    {
        _bestScore = Math.Max(_bestScore, _score);
        _score = 0;
        _wave = 0;
        _wavePauseTimer = 2.5f;
        _playerHp = MaxHp;
        _playerPosition = new Vector3(0f, 0f, PlayerZ);
        _invulnerabilityTimer = 2f;
        for (int i = _enemies.Count - 1; i >= 0; i--)
            KillEnemy(_enemies[i]);
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            _bullets[i].Trail?.Finish(_bullets[i].Position);
            RemoveBullet(i);
        }
    }

    private void TickScriptedDemo()
    {
        if (_tickCount == 5)
        {
            for (int i = 0; i < 9; i++)
            {
                float angle = i * MathF.PI * 2f / 9f + 0.4f;
                SpawnEnemy(new Vector3(MathF.Cos(angle) * 15.5f, MathF.Sin(angle) * 15.5f, 0.6f));
            }
        }
        Enemy? nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (Enemy enemy in _enemies)
        {
            float distance = Vector3.DistanceSquared(enemy.Position, _playerPosition);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = enemy;
            }
        }
        if (nearest != null)
        {
            Vector2 toEnemy = new(nearest.Position.X - _playerPosition.X, nearest.Position.Y - _playerPosition.Y);
            if (toEnemy.LengthSquared() > 0.04f)
                _aimDirection = Vector2.Normalize(toEnemy);
        }
    }

    private static void PushOutOfCollidersImpl(List<Box> colliders, ref Vector3 position, float radius)
    {
        foreach (Box box in colliders)
        {
            if (position.Z + radius * 0.5f <= box.Min.Z || position.Z - radius * 0.5f >= box.Max.Z)
                continue;
            Vector2 closest = new(
                Math.Clamp(position.X, box.Min.X, box.Max.X),
                Math.Clamp(position.Y, box.Min.Y, box.Max.Y));
            Vector2 toMover = new Vector2(position.X, position.Y) - closest;
            float distance = toMover.Length();
            if (distance < radius && distance > 1e-5f)
            {
                Vector2 push = toMover / distance * (radius - distance);
                position += new Vector3(push, 0f);
            }
        }
    }

    private void PushOutOfColliders(ref Vector3 position, float radius)
        => PushOutOfCollidersImpl(_colliders, ref position, radius);

    private void DrawHud()
    {
        ImDrawListPtr drawList = ImGui.GetForegroundDrawList();
        uint white = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.9f));

        if (_screenshotPath == null)
        {
            Vector2 mouse = MainView.MousePosition;
            uint crossColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f));
            const float arm = 8f;
            drawList.AddCircle(mouse, 5.5f, crossColor, 16, 1.5f);
            drawList.AddLine(mouse + new Vector2(-arm - 4, 0), mouse + new Vector2(-6, 0), crossColor, 1.5f);
            drawList.AddLine(mouse + new Vector2(6, 0), mouse + new Vector2(arm + 4, 0), crossColor, 1.5f);
            drawList.AddLine(mouse + new Vector2(0, -arm - 4), mouse + new Vector2(0, -6), crossColor, 1.5f);
            drawList.AddLine(mouse + new Vector2(0, 6), mouse + new Vector2(0, arm + 4), crossColor, 1.5f);
        }

        string status = $"FPS {FrameRate}   score {_score}   best {_bestScore}   wave {Math.Max(_wave, 1)}   drones {_enemies.Count}   hits {_hits}/{_shots}";
        drawList.AddText(new Vector2(12, 10), white, status);
        if (_screenshotPath == null)
        {
            drawList.AddText(new Vector2(12, 26), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.55f)),
                "WASD move   mouse aim   LMB fire   ESC exit");
        }

        Vector2 barPos = new(12, MainView.Size.Y - 30f);
        Vector2 barSize = new(220, 14);
        float fraction = Math.Clamp(_playerHp / MaxHp, 0f, 1f);
        Vector4 hpColor = fraction > 0.35f
            ? new Vector4(0.3f, 0.9f, 0.5f, 0.9f)
            : new Vector4(1f, 0.25f, 0.15f, 0.9f);
        drawList.AddRectFilled(barPos, barPos + barSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.55f)), 3f);
        drawList.AddRectFilled(barPos, barPos + new Vector2(barSize.X * fraction, barSize.Y),
            ImGui.ColorConvertFloat4ToU32(hpColor), 3f);
        drawList.AddRect(barPos, barPos + barSize, white, 3f);

        if (_waveBannerTimer > 0f)
        {
            string banner = $"WAVE {Math.Max(_wave, 1)}";
            Vector2 bannerPos = new(MainView.Size.X * 0.5f - 40, 60);
            drawList.AddText(bannerPos + new Vector2(1, 1), ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.7f)), banner);
            drawList.AddText(bannerPos, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.85f, 0.4f, 0.95f)), banner);
        }
        if (_invulnerabilityTimer > 1.2f && _screenshotPath == null)
        {
            string note = "SHIP DOWN — respawning";
            Vector2 notePos = new(MainView.Size.X * 0.5f - 90, MainView.Size.Y * 0.4f);
            drawList.AddText(notePos + new Vector2(1, 1), ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.7f)), note);
            drawList.AddText(notePos, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.4f, 0.3f, 0.95f)), note);
        }
    }

    /// <inheritdoc />
    protected override void OnStop()
    {
        GraphicsDevice.WaitIdle();
        _trails.Dispose();
        _boxBuffers[0].Destroy();
        _boxBuffers[1].Destroy();
        _renderGraph.Destroy();
        _renderContext.Destroy();
    }

    private static bool RayBox(Vector3 from, Vector3 to, in Box box, out float t)
    {
        t = 0f;
        float tMax = 1f;
        Vector3 d = to - from;
        for (int axis = 0; axis < 3; axis++)
        {
            float origin = axis == 0 ? from.X : axis == 1 ? from.Y : from.Z;
            float direction = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            float min = axis == 0 ? box.Min.X : axis == 1 ? box.Min.Y : box.Min.Z;
            float max = axis == 0 ? box.Max.X : axis == 1 ? box.Max.Y : box.Max.Z;
            if (MathF.Abs(direction) < 1e-8f)
            {
                if (origin < min || origin > max)
                    return false;
                continue;
            }
            float inv = 1f / direction;
            float t1 = (min - origin) * inv;
            float t2 = (max - origin) * inv;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            t = MathF.Max(t, t1);
            tMax = MathF.Min(tMax, t2);
            if (t > tMax)
                return false;
        }
        return true;
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

    private static string? GetArgValue(string[] args, string prefix)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
                return arg[prefix.Length..];
        }
        return null;
    }

    private sealed class ArenaSceneNode(Game game) : RenderGraphNode("ArenaScene3D")
    {
        private RGTexture _colorTarget;
        public RGTexture DepthTarget { get; private set; }

        protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
        {
            _colorTarget = builder.Output;
            DepthTarget = builder.CreateTexture(new RGTextureDesc
            {
                Name = "ArenaDepth",
                Format = Format.D32Float,
                Usage = TextureUsage.DepthStencilAttachment,
            });
            pass.Write(_colorTarget, RGUsage.ColorAttachmentWrite);
            pass.Write(DepthTarget, RGUsage.DepthStencil);
        }

        protected override void ExecutePass(in RenderGraphContext context)
        {
            int count = Math.Min(game._sceneBoxes.Count, MaxArenaBoxes);
            for (int i = 0; i < count; i++)
            {
                SceneBox box = game._sceneBoxes[i];
                game._boxStaging[i] = new ArenaBoxInstanceData
                {
                    WorldMatrix = box.Transform.Matrix,
                    BaseColor = new Vector4(box.BaseColor, 1f),
                    EmissiveChecker = new Vector4(box.Emissive, box.IsCheckerGround ? 1f : 0f),
                };
            }

            Alco.Graphics.Buffer boxBuffer = game._boxBuffers[game._frameParity];
            game._frameParity ^= 1;
            boxBuffer.SetData<ArenaBoxInstanceData>(game._boxStaging.AsSpan(0, count));

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
                        Clear = new Vector4(0.08f, 0.06f, 0.07f, 1f),
                    },
                ],
                Depth = new DepthAttachment
                {
                    RenderView = context.GetRenderView(DepthTarget),
                    Load = LoadOp.Clear,
                    Store = StoreOp.Store,
                    Clear = 0.0f,
                },
            };
            Extent2D vp = context.Viewport;

            GPUAllocation<byte> alloc = rc.FrameData.Allocate((ulong)sizeof(ArenaRootData));
            var root = new ArenaRootData
            {
                ViewProjection = game._camera.ViewProjectionMatrix,
                SunDirIntensity = new Vector4(Vector3.Normalize(new Vector3(-0.65f, -0.52f, 0.55f)), 2.2f),
                SunColor = new Vector4(1.0f, 0.72f, 0.42f, 1.0f),
                SkyHorizon = new Vector4(0.55f, 0.42f, 0.36f, 1.0f),
                SkyZenith = new Vector4(0.24f, 0.34f, 0.52f, 1.0f),
                Instances = boxBuffer.View,
            };
            MemoryMarshal.Write(alloc.CpuSpan, in root);

            using RenderContext.RenderScope render = rc.BeginRenderPass(desc);
            render.SetViewport(new Viewport { Width = vp.X, Height = vp.Y });
            render.SetScissor(new Scissor { Width = vp.X, Height = vp.Y });
            render.Draw(
                game._arenaMaterial,
                alloc.GPURange,
                DepthStencilState.WriteReverseZ,
                vertexCount: 36,
                instanceCount: (uint)count);
        }
    }
}
