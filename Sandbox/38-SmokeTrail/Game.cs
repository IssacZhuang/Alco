using System.Diagnostics;
using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.IO;
using Alco.Rendering;
using Alco.World3D;
using SandboxUtils;

/// <summary>
/// Top-down twin-stick arena shooter built on the deferred PBR pipeline
/// (Alco.World3D): a golden-hour sun with cascaded shadow maps, a physical sky
/// gradient for ambient, HBAO, HDR bloom, ACES tonemapping and FXAA. The player
/// ship hovers over a walled arena scattered with crates and glowing corner
/// pylons; enemy drones spawn in waves and kamikaze-chase the ship.
/// <br/>The sandbox's signature feature — the 3D GPU trail renderer
/// (<see cref="GpuTrailSystem3D"/>, Alco.Rendering) — drives the bullets: every
/// shot is an emissive tracer that drags a camera-facing smoke ribbon
/// (TrailSurfaceSmoke: turbulence wander, buoyant rise, noise-eroded
/// dissipation), and every destroyed drone bursts into a short smoke puff from
/// the same system. Muzzle flashes and drone explosions are pooled point lights.
/// <br/>Controls: WASD move, mouse aim, left mouse fires, ESC exits.
/// <br/>Frame model: the render loop is uncapped. Player movement/aim and the
/// chase camera run per frame (<see cref="OnUpdate"/>) for input responsiveness
/// and smooth visuals; the simulation (shooting, bullets, drones, waves, damage)
/// runs on the engine's fixed 60 Hz tick (<see cref="OnTick"/>), and tick-driven
/// bodies interpolate their render transforms between ticks, so gameplay speed
/// and hit detection are identical at any render rate.
/// <br/>CLI: --screenshot=&lt;path.png&gt; [--frames=N] — runs a scripted demo
/// (a ring of drones converges on the ship, which auto-fires at the nearest one)
/// and captures the presented frame after N simulation ticks.
/// </summary>
public class Game : GameEngine
{
    // ── Rendering ────────────────────────────────────────────────────────────
    private readonly CameraPerspectiveBuffer _camera;
    private readonly MaterialCompiler _materialCompiler;
    private readonly PBRDeferredPreset _preset;
    private readonly PBRSceneEnvironment _environment;
    private readonly GBufferRenderer _gbufferRenderer;
    private readonly ShadowRenderer _shadowRenderer;
    private readonly GpuTrailSystem3D _trails;
    private readonly TrailEffect3D _bulletTrailEffect;
    private readonly TrailEffect3D _puffTrailEffect;
    private readonly PrimitiveMesh _cubeMesh;

    private const float CameraNear = 0.1f;
    private const float CameraFov = 0.78f;
    // The camera hovers north-above the ship looking south-down: screen-right is
    // world +X and screen-up is world -Y, and the tilt shows the lit south faces
    // and the long golden-hour shadows of the props.
    private static readonly Vector3 CameraOffset = new(0f, 12.5f, 19f);

    // ── Scene objects (the adapter the deferred renderers batch and draw) ─────
    private sealed class SceneObject : IGBufferRenderable, IShadowRenderable
    {
        public required PrimitiveMesh Mesh;
        public Transform3D Transform = Transform3D.Identity;
        public Vector3 BaseColor;
        public float Metallic;
        public float Roughness;
        public Vector3 Emissive;
        public bool CastsShadow = true;
        public bool Static;

        public PbrMaterialAsset Material { get; set; } = PbrMaterialAsset.Default;
        public bool IsStatic => Static;
        Mesh IGBufferRenderable.Mesh => Mesh;
        Matrix4x4 IGBufferRenderable.WorldMatrix => Transform.Matrix;
        Vector4 IGBufferRenderable.BaseColor => new(BaseColor, 1f);
        Vector4 IGBufferRenderable.MetallicRoughnessAO => new(Metallic, Roughness, 1f, 1f);
        Vector3 IGBufferRenderable.EmissiveFactor => Emissive;
        float IGBufferRenderable.AlphaCutoff => 0f;

        bool IShadowRenderable.IsStatic => Static;
        bool IShadowRenderable.CastsShadow => CastsShadow;
        Mesh IShadowRenderable.Mesh => Mesh;
        Matrix4x4 IShadowRenderable.WorldMatrix => Transform.Matrix;
        float IShadowRenderable.AlphaCutoff => 0f;
        float IShadowRenderable.BaseColorAlpha => 1f;
        Vector4 IShadowRenderable.RsmBaseColor => new(BaseColor, 1f);
    }

    // ── World: static props double as the movement/bullet collision set ──────
    private readonly record struct Box(Vector3 Min, Vector3 Max)
    {
        public Vector3 Center => (Min + Max) * 0.5f;
        public Vector3 Size => Max - Min;
    }

    private readonly List<Box> _colliders = [];
    private readonly Vector3[] _lampPositions = new Vector3[4];

    private const float ArenaHalf = 21f;
    private const float BoundsHalf = 20.2f;

    // ── Player ────────────────────────────────────────────────────────────────
    private readonly SceneObject _playerBody;
    private readonly SceneObject _playerVisor;
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

    // ── Bullets ──────────────────────────────────────────────────────────────
    private sealed class Bullet
    {
        public required Vector3 Position;
        public Vector3 PreviousPosition;
        public required Vector3 Direction;
        public required SceneObject Body;
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

    // ── Enemies (drones) ─────────────────────────────────────────────────────
    private sealed class Enemy
    {
        public required Vector3 Position;
        public Vector3 PreviousPosition;
        public required SceneObject Body;
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

    // ── Death effects: pooled explosion lights and smoke puffs ───────────────
    private sealed class Puff
    {
        public required TrailEffectInstance3D Trail;
        public Vector3 Position;
        public float EmitTimer;
    }

    private readonly List<Puff> _puffs = [];
    private readonly List<(Vector3 Position, float Timer)> _explosions = [];
    private readonly PBRSceneEnvironment.PointLight[] _pointLights =
        new PBRSceneEnvironment.PointLight[16];
    private FastRandom _random = new(987654);
    private float _time;

    // ── Screenshot mode (see the class remarks) ──────────────────────────────
    private readonly string? _screenshotPath;
    private readonly int _screenshotFrames;
    private SwapchainCaptureSystem? _swapchainCapture;
    private Task<RenderCaptureResult>? _screenshotRequest;
    private bool _screenshotSaved;

    // ── Fixed-tick bookkeeping ───────────────────────────────────────────────
    // The simulation runs on the engine's fixed 60 Hz tick (OnTick); OnUpdate
    // runs per rendered frame (uncapped). RenderAlpha is the fraction of the
    // current tick interval elapsed — tick-simulated bodies interpolate their
    // render transforms between the previous and current tick positions.
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

    public Game(GameEngineSetting setting, string[] args) : base(setting)
    {
        _screenshotPath = GetArgValue(args, "--screenshot=");
        _screenshotFrames = int.TryParse(GetArgValue(args, "--frames="), out int frames) ? frames : 150;
        if (_screenshotPath != null)
        {
            _swapchainCapture = SwapchainCapture;
            // The demo aims itself; keep the cursor out of the capture.
            Input.IsCursorVisible = false;
        }

        AddSystem(new ImGUISystem(this));

        // Reversed infinite-far depth: the deferred G-buffer clears depth to 0
        // and tests GreaterEqual.
        _camera = RenderingSystem.CreateCameraPerspective(CameraFov, 16f / 9, CameraNear, 4096f);
        _camera.ReverseInfiniteDepth = true;

        _materialCompiler = World3DAssetPipeline.CreateMaterialCompiler(RenderingSystem);

        _preset = RenderPipelines.CreatePBRDeferred(
            RenderingSystem,
            RenderingSystem.ShaderSystem.GetShader("DeferredLighting"),
            RenderingSystem.ShaderSystem.GetLibrary("AlcoWorld3D_PBRCommon"),
            BuiltInAssets.Shader_Blit,
            shadowMapSize: 2048,
            width: (uint)MainView.Size.X,
            height: (uint)MainView.Size.Y);
        _environment = _preset.Environment;
        _environment.Camera = _camera;

        _gbufferRenderer = new GBufferRenderer(
            RenderingSystem, _materialCompiler, RenderingSystem.ShaderSystem.GetLibrary("GBuffer"));
        _gbufferRenderer.SetCamera(_camera);
        _shadowRenderer = new ShadowRenderer(
            RenderingSystem,
            _materialCompiler,
            RenderingSystem.ShaderSystem.GetLibrary("ShadowDepth"),
            RenderingSystem.ShaderSystem.GetLibrary("Rsm"),
            _preset.ShadowLayout,
            _environment.ShadowDataBuffer);
        _preset.GBufferPass.Content.Add(_gbufferRenderer);
        _preset.ShadowPass.Content.Add(_shadowRenderer);

        // HBAO+ for contact grounding; the factory asset supplies the shader
        // bindings and Attach wires the node and the lighting AO input.
        var nodeServices = new RenderNodeFactoryServices()
            .Add(_preset.PostChain)
            .Add(_preset.PostProcessLayout)
            .Add(_materialCompiler)
            .Add(_camera)
            .Add(_environment);
        var nodeFactoryContext = new RenderNodeFactoryContext(RenderingSystem, _preset.Graph, nodeServices);
        var hbaoFactory = AssetSystem.Load<RenderNodeFactory>("RenderNodes/HBAO.rnfact") as RGNodeFactory_HBAO
            ?? throw new InvalidDataException("RenderNodes/HBAO.rnfact is not an RGNodeFactory_HBAO.");
        hbaoFactory.CreateNode<RGNode_HBAO>(nodeFactoryContext)
            .Attach(_preset.Graph, _preset.Lighting, _preset.GBufferResource, _environment);

        // The 3D trail system: one shared point buffer for bullet ribbons and
        // death puffs. Ribbons face CameraPosition, updated per frame.
        _trails = new GpuTrailSystem3D(RenderingSystem, pointCapacity: 1 << 17, trailSlots: 256)
        {
            Camera = _camera,
        };
        _bulletTrailEffect = new TrailEffect3D
        {
            Life = 1.8f,
            Spacing = 0.35f,
            // Constant base width with the shared smoke surface's turbulence
            // and age erosion.
            Width0 = 0.18f,
            Width1 = 0.18f,
            Opacity = 1f,
            FadeIn = 0.04f,
            FadeOut = 0.55f,
            ExpectedPoints = 512,
            // The deferred scene color shares the G-buffer depth (reverse-Z).
            Depth = DepthStencilState.ReadReverseZ,
            Material = new MaterialAsset
            {
                Name = "bullet-smoke",
                Surface = RenderingSystem.ShaderSystem.GetLibrary("TrailSurfaceSmoke"),
                Parameters = new Dictionary<string, ShaderValue>
                {
                    ["smokeColor"] = new Vector4(0.45f, 0.48f, 0.55f, 0.65f),
                    ["shape"] = new Vector4(0.35f, 0.35f, 0.06f, 0.45f),
                    ["width"] = new Vector4(0.12f, 0.5f, 0f, 0f),
                    ["edge"] = new Vector4(0.05f, 1.4f, 0.6f, 0f),
                },
            },
        };
        // The drone death puff: a stationary emitter whose overlapping points
        // rise and erode into a single warm blast cloud.
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
                Surface = RenderingSystem.ShaderSystem.GetLibrary("TrailSurfaceSmoke"),
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

        // Node order after deferred lighting: trails (alpha-blended into the HDR
        // scene color, depth-tested against the G-buffer) → bloom → ACES → FXAA.
        _preset.Pipeline.Use(new TrailNode(_trails, _preset.Graph, _preset.PostChain));
        _preset.Pipeline.Use(new RGNode_Bloom(
            RenderingSystem,
            _preset.Graph,
            _preset.PostChain,
            _preset.PostProcessLayout,
            new RGNode_Bloom.Descriptor
            {
                BlitShader = BuiltInAssets.Shader_BloomBlit,
                ClampShader = BuiltInAssets.Shader_BloomClamp,
                DownsampleShader = BuiltInAssets.Shader_BloomDownsample,
                UpsampleShader = BuiltInAssets.Shader_BloomUpsample,
                SceneCopyShader = BuiltInAssets.Shader_Blit,
                Threshold = 0.7f,
                Intensity = 0.9f,
                Spread = 1.1f,
            }));
        var tonemapNode = new RGNode_Tonemap(
            RenderingSystem,
            _preset.Graph,
            _preset.PostChain,
            _preset.PostProcessLayout,
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
        acesData.Exposure = 0.95f;
        acesData.Gamma = 2.2f;
        tonemapNode.ACESData = acesData;
        _preset.Pipeline.Use(tonemapNode);
        _preset.Pipeline.Use(new RGNode_FXAA(
            RenderingSystem,
            _preset.Graph,
            _preset.PostChain,
            _preset.PostProcessLayout,
            new RGNode_FXAA.Descriptor
            {
                SceneCopyShader = BuiltInAssets.Shader_Blit,
                FxaaShader = BuiltInAssets.Shader_FXAA,
            }));

        MainPresenter.OnResize += size =>
        {
            _camera.AspectRatio = (float)size.X / size.Y;
            _preset.Pipeline.Resize(size.X, size.Y);
        };

        SetupGoldenHourLighting();
        _cubeMesh = CreateCubeMesh();
        BuildWorld();

        // The player ship: a metallic blue hull with an emissive cyan visor at
        // the nose; both yaw toward the mouse aim every frame.
        _playerBody = new SceneObject
        {
            Mesh = _cubeMesh,
            Static = false,
            BaseColor = new Vector3(0.3f, 0.55f, 0.85f),
            Metallic = 0.85f,
            Roughness = 0.3f,
            Transform = new Transform3D(_playerPosition, Quaternion.Identity, new Vector3(1.15f, 0.72f, 0.38f)),
        };
        _playerVisor = new SceneObject
        {
            Mesh = _cubeMesh,
            Static = false,
            CastsShadow = false,
            BaseColor = new Vector3(0.05f, 0.1f, 0.12f),
            Metallic = 0.2f,
            Roughness = 0.2f,
            Emissive = new Vector3(0.5f, 4.5f, 5.5f),
        };
        _gbufferRenderer.Add(_playerBody);
        _shadowRenderer.Add(_playerBody);
        _gbufferRenderer.Add(_playerVisor);
    }

    /// <summary>
    /// Fixes the time of day at golden hour: a warm low sun (long soft shadows),
    /// its color and intensity derived from the atmosphere transmittance, and the
    /// matching sky radiance gradient as ambient.
    /// </summary>
    private void SetupGoldenHourLighting()
    {
        Vector3 directionToSun = ProceduralSkyUtility.GetDirectionToSun(17.1f, 48f, -0.9f);
        Vector3 sunTint = ProceduralSkyUtility.GetSunColor(directionToSun);
        // Artistic warm tint so the evening light reads golden, not neutral.
        sunTint = Vector3.Lerp(sunTint, sunTint * new Vector3(1.0f, 0.72f, 0.45f), 0.6f);
        float sunScale = ProceduralSkyUtility.GetSunLightScale(directionToSun);
        ProceduralSkyUtility.GetSkyRadianceGradient(
            directionToSun, 1.0f, 0.35f, 0.6f, 0.03f, 20f,
            out Vector3 skyHorizonColor, out Vector3 skyZenithColor);

        _environment.SunDirection = -directionToSun;
        _environment.SunColor = sunTint;
        _environment.SunIntensity = 9f * sunScale;
        _environment.SkyHorizonColor = skyHorizonColor;
        _environment.SkyZenithColor = skyZenithColor;
        _environment.SkyParams = new Vector4(1.0f, 0.35f, 0.88f, 0.6f);
        _environment.SkyParams2 = new Vector4(0.7f, 0.03f, 20f, 0.14f);

        // Arena-scale shadows: a short distance keeps the cascades sharp.
        _environment.ShadowDistance = 75f;
        _environment.ShadowCasterExtension = 75f;
        _environment.ShadowSplitLambda = 0.65f;
    }

    /// <summary>
    /// The arena: a checkered concrete floor, perimeter walls with glowing top
    /// strips, scattered cover crates and four corner pylons carrying cyan lamps
    /// (emissive cubes + static point lights) that contrast the warm sun.
    /// </summary>
    private void BuildWorld()
    {
        // The floor (top at z = 0; it only receives shadows).
        Texture2D checker = CreateCheckerTexture(256);
        var groundAsset = new PbrMaterialAsset
        {
            Name = "ground",
            Textures = new Dictionary<string, Texture2D> { ["albedoTexture"] = checker },
        };
        var ground = new SceneObject
        {
            Mesh = CreateGroundMesh(120f, 8, 8f),
            Static = true,
            CastsShadow = false,
            BaseColor = new Vector3(0.16f, 0.17f, 0.2f),
            Metallic = 0.05f,
            Roughness = 0.92f,
            Material = groundAsset,
        };
        _gbufferRenderer.Add(ground);

        // Perimeter walls (collision boxes included) and their emissive top trim.
        (Vector3 Center, Vector3 Size)[] walls =
        [
            (new Vector3(ArenaHalf + 0.5f, 0, 1.2f), new Vector3(1, 44.5f, 2.4f)),
            (new Vector3(-ArenaHalf - 0.5f, 0, 1.2f), new Vector3(1, 44.5f, 2.4f)),
            (new Vector3(0, ArenaHalf + 0.5f, 1.2f), new Vector3(44.5f, 1, 2.4f)),
            (new Vector3(0, -ArenaHalf - 0.5f, 1.2f), new Vector3(44.5f, 1, 2.4f)),
        ];
        foreach ((Vector3 center, Vector3 size) in walls)
        {
            AddStaticBox(center, size, new Vector3(0.26f, 0.27f, 0.3f), 0.65f, 0.5f, collides: true);
        }
        (Vector3 Center, Vector3 Size)[] trims =
        [
            (new Vector3(ArenaHalf, 0, 2.46f), new Vector3(0.2f, 43f, 0.12f)),
            (new Vector3(-ArenaHalf, 0, 2.46f), new Vector3(0.2f, 43f, 0.12f)),
            (new Vector3(0, ArenaHalf, 2.46f), new Vector3(43f, 0.2f, 0.12f)),
            (new Vector3(0, -ArenaHalf, 2.46f), new Vector3(43f, 0.2f, 0.12f)),
        ];
        foreach ((Vector3 center, Vector3 size) in trims)
        {
            AddStaticBox(center, size, new Vector3(0.3f, 0.15f, 0.05f), 0f, 0.4f,
                collides: false, castsShadow: false, emissive: new Vector3(3.0f, 1.4f, 0.45f));
        }

        // Cover crates: wood, steel and olive boxes of varying sizes.
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
        float[] kindMetallic = [0f, 0.9f, 0.15f];
        float[] kindRoughness = [0.8f, 0.35f, 0.7f];
        foreach ((Vector3 center, Vector3 size, int kind) in crates)
        {
            AddStaticBox(center, size, kindColors[kind], kindMetallic[kind], kindRoughness[kind], collides: true);
        }

        // Corner pylons: a dark metal mast, an emissive cyan lamp head and a
        // matching static point light (the cool fill against the warm sun).
        Vector2[] corners = [new(17, 17), new(-17, 17), new(17, -17), new(-17, -17)];
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 c = corners[i];
            AddStaticBox(new Vector3(c, 1.7f), new Vector3(1.7f, 1.7f, 3.4f),
                new Vector3(0.2f, 0.21f, 0.25f), 0.85f, 0.4f, collides: true);
            Vector3 lamp = new(c.X, c.Y, 3.7f);
            AddStaticBox(lamp, new Vector3(1.0f, 1.0f, 0.55f), new Vector3(0.06f, 0.1f, 0.12f),
                0.3f, 0.25f, collides: false, castsShadow: false, emissive: new Vector3(0.6f, 3.5f, 4.2f));
            _lampPositions[i] = lamp + new Vector3(0, 0, 0.6f);
        }
    }

    /// <summary>Registers a static box prop and optionally its collision volume.</summary>
    private void AddStaticBox(Vector3 center, Vector3 size, Vector3 color, float metallic, float roughness,
        bool collides, bool castsShadow = true, Vector3 emissive = default)
    {
        var obj = new SceneObject
        {
            Mesh = _cubeMesh,
            Static = true,
            CastsShadow = castsShadow,
            BaseColor = color,
            Metallic = metallic,
            Roughness = roughness,
            Emissive = emissive,
            Transform = new Transform3D(center, Quaternion.Identity, size),
        };
        _gbufferRenderer.Add(obj);
        if (castsShadow)
        {
            _shadowRenderer.Add(obj);
        }
        if (collides)
        {
            Vector3 half = size * 0.5f;
            _colliders.Add(new Box(center - half, center + half));
        }
    }

    /// <summary>
    /// Per-frame body (uncapped): input-responsive player movement and aim, the
    /// chase camera, interpolated render transforms for the tick-simulated
    /// bodies, visual effect decay, lighting uploads, the HUD and the render.
    /// </summary>
    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }
        _time += delta;

        UpdatePlayer(delta);
        UpdateCamera(delta);
        ApplyRenderTransforms(RenderAlpha);

        // Visual-only timers decay per frame for smoothness.
        _muzzleFlash = MathF.Max(0f, _muzzleFlash - delta / 0.07f);
        if (_waveBannerTimer > 0f)
        {
            _waveBannerTimer -= delta;
        }

        // Per-frame lighting: fit the cascades to the following camera, then
        // upload the lamps, muzzle flash and live explosions.
        _environment.ComputeShadowCascades(CameraNear);
        UploadPointLights(delta);

        _trails.CameraPosition = _camera.Transform.Position;
        _trails.Update(delta);

        DrawHud();
        DebugStats.Text(FrameRate);

        if (_screenshotPath != null && _screenshotRequest == null && !_screenshotSaved && _tickCount >= _screenshotFrames)
        {
            // The request completes a couple of frames later with the presented
            // frame's PNG (scene, post processing and the ImGui overlay on top).
            _screenshotRequest = _swapchainCapture!.RequestCaptureAsync();
        }

        _preset.Pipeline.Render(MainPresenter.FrameBuffer);

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

    /// <summary>
    /// Fixed 60 Hz simulation tick (the engine's physics tick): shooting cadence,
    /// bullet kinematics and collision, drone AI and contact damage, waves, puff
    /// emission and gameplay timers. Frame-rate independent by construction, so
    /// the game plays identically at any render rate.
    /// </summary>
    protected override void OnTick(float delta)
    {
        _lastTickTimestamp = Stopwatch.GetTimestamp();
        _tickCount++;

        if (_screenshotPath != null)
        {
            TickScriptedDemo();
        }
        TickShooting(delta);
        TickBullets(delta);
        TickEnemies(delta);
        TickPuffs(delta);
        TickWaves(delta);

        if (_invulnerabilityTimer > 0f)
        {
            _invulnerabilityTimer -= delta;
        }
    }

    /// <summary>The chase camera: a fixed north-above offset smoothly tracking the ship.</summary>
    private void UpdateCamera(float delta)
    {
        float blend = 1f - MathF.Exp(-8f * delta);
        _cameraFocus = Vector3.Lerp(_cameraFocus, _playerPosition, blend);
        Vector3 eye = _cameraFocus + CameraOffset;
        Vector3 forward = Vector3.Normalize(_cameraFocus - eye);
        _camera.Transform = new Transform3D(eye, LookRotation(forward, Vector3.UnitZ));
        _camera.UpdateMatrixToGPU();
    }

    /// <summary>
    /// WASD in the camera's ground plane, a circle-vs-AABB push-out against the
    /// props, arena clamping, and the mouse aim: the cursor's screen ray is
    /// intersected with the ship's flight plane to get the world aim point.
    /// </summary>
    private void UpdatePlayer(float delta)
    {
        // Movement basis on the ground: the camera's forward/right flattened to XY.
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
        {
            _playerPosition += new Vector3(Vector2.Normalize(move), 0f) * MoveSpeed * delta;
        }

        PushOutOfColliders(ref _playerPosition, PlayerRadius);
        _playerPosition.X = Math.Clamp(_playerPosition.X, -BoundsHalf, BoundsHalf);
        _playerPosition.Y = Math.Clamp(_playerPosition.Y, -BoundsHalf, BoundsHalf);
        _playerPosition.Z = PlayerZ;

        // Aim: mouse ray onto the flight plane; keep the last direction when the
        // cursor sits on the ship. The scripted demo steers the aim itself.
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
                    {
                        _aimDirection = Vector2.Normalize(toAim);
                    }
                }
            }
        }
        _playerYaw = MathF.Atan2(_aimDirection.Y, _aimDirection.X);

        Quaternion yawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, _playerYaw);
        _playerBody.Transform = new Transform3D(_playerPosition, yawRotation, new Vector3(1.15f, 0.72f, 0.38f));
        Vector3 visorOffset = Vector3.Transform(new Vector3(0.46f, 0f, 0.16f), yawRotation);
        _playerVisor.Transform = new Transform3D(_playerPosition + visorOffset, yawRotation, new Vector3(0.4f, 0.42f, 0.16f));
    }

    /// <summary>
    /// Unprojects a window-local pixel position to a world ray from the camera.
    /// Implemented locally because <c>CameraMathUtility.ScreenPointToDirectionPerspective</c>
    /// forgets to subtract the camera position, so it only works for a camera at the origin.
    /// </summary>
    private Ray3D ScreenPointToWorldRay(Vector2 screenPoint)
    {
        Vector2 size = new(MainView.Size.X, MainView.Size.Y);
        Vector2 clip = new(2f * screenPoint.X / size.X - 1f, 1f - 2f * screenPoint.Y / size.Y);
        Matrix4x4.Invert(_camera.Data.ViewProjectionMatrix, out Matrix4x4 inverseViewProjection);
        Vector4 point = Vector4.Transform(new Vector4(clip, 1f, 1f), inverseViewProjection);
        if (point.W != 0f)
        {
            point /= point.W;
        }
        Vector3 cameraPosition = _camera.Transform.Position;
        return new Ray3D(cameraPosition, Vector3.Normalize(new Vector3(point.X, point.Y, point.Z) - cameraPosition));
    }

    /// <summary>
    /// Render transforms for the tick-simulated bodies: positions interpolate
    /// between the previous and current tick by the tick-interval fraction, so
    /// drones and tracers glide smoothly at any render rate; the bob, spin and
    /// core pulse are visual-only and key off the frame clock.
    /// </summary>
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
            enemy.Body.Emissive = new Vector3(4.0f, 0.5f, 0.12f) * pulse;
        }
        foreach (Bullet bullet in _bullets)
        {
            Vector3 position = Vector3.Lerp(bullet.PreviousPosition, bullet.Position, alpha);
            bullet.Body.Transform = new Transform3D(position, bullet.Body.Transform.Rotation, bullet.Body.Transform.Scale);
        }
    }

    /// <summary>Left mouse fires at a fixed cadence toward the aim direction (tick-driven).</summary>
    private void TickShooting(float delta)
    {
        _fireCooldown -= delta;

        bool wantsFire = _screenshotPath != null
            ? _enemies.Count > 0 && _tickCount >= 70
            : MainView.IsFocused && Input.IsMousePressing(Mouse.Left);
        if (!wantsFire || _fireCooldown > 0f)
        {
            return;
        }
        _fireCooldown = FireInterval;

        Vector3 direction = new(_aimDirection, 0f);
        Vector3 muzzle = _playerPosition + direction * 0.75f;
        muzzle.Z = PlayerZ;
        _lastMuzzle = muzzle;
        _muzzleFlash = 1f;

        var body = new SceneObject
        {
            Mesh = _cubeMesh,
            Static = false,
            CastsShadow = false,
            BaseColor = new Vector3(1f, 0.6f, 0.2f),
            Metallic = 0f,
            Roughness = 0.4f,
            Emissive = new Vector3(6f, 2.5f, 0.8f),
            Transform = new Transform3D(muzzle, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, _playerYaw),
                new Vector3(0.32f, 0.15f, 0.15f)),
        };
        _gbufferRenderer.Add(body);

        if (!_trails.TryCreateInstance(_bulletTrailEffect, muzzle, out TrailEffectInstance3D trail))
        {
            // The trail budget is exhausted: skip the shot's trail, not the shot.
            trail = null!;
        }
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

    /// <summary>
    /// Integrates the bullets with a segment sweep against the props and the
    /// drones (tick-driven). On a hit the trail finishes at the impact point and
    /// fades; in flight the head extends the ribbon. The render transform lerps
    /// between tick positions in <see cref="ApplyRenderTransforms"/>.
    /// </summary>
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
        _gbufferRenderer.Remove(_bullets[index].Body);
        _bullets.RemoveAt(index);
    }

    /// <summary>
    /// The nearest intersection of the segment with the collision boxes (slab
    /// test) and the drones (closest-approach sphere test). Returns the hit point
    /// and the hit drone (null for static geometry).
    /// </summary>
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
            {
                continue;
            }
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

    /// <summary>
    /// The drones (tick-driven): chase the ship, keep off each other and the
    /// props, and detonate on contact (damaging the ship unless it is briefly
    /// invulnerable). Destroyed drones score and burst into light and smoke. The
    /// render transform (bob, spin, core pulse) lives in ApplyRenderTransforms.
    /// </summary>
    private void TickEnemies(float delta)
    {
        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            Enemy enemy = _enemies[i];
            enemy.PreviousPosition = enemy.Position;
            Vector3 toPlayer = _playerPosition - enemy.Position;
            float distance = new Vector2(toPlayer.X, toPlayer.Y).Length();
            if (distance > 1e-4f)
            {
                enemy.Position += toPlayer / distance * enemy.Speed * delta;
            }

            // Pairwise separation so the pack does not stack into one drone.
            for (int j = 0; j < _enemies.Count; j++)
            {
                if (j == i)
                {
                    continue;
                }
                Vector2 apart = new(enemy.Position.X - _enemies[j].Position.X, enemy.Position.Y - _enemies[j].Position.Y);
                float apartLength = apart.Length();
                if (apartLength is > 1e-4f and < 1.1f)
                {
                    enemy.Position += new Vector3(apart / apartLength * (1.1f - apartLength) * 0.5f, 0f);
                }
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
                    // The reset clears the whole enemy list — leave the loop.
                    OnPlayerDeath();
                    break;
                }
                continue;
            }
        }
    }

    /// <summary>Wave spawner: a short breather, then a bigger pack from the arena edges.</summary>
    private void TickWaves(float delta)
    {
        if (_screenshotPath != null)
        {
            return; // The scripted demo spawns its own ring once.
        }
        if (_enemies.Count > 0)
        {
            return;
        }
        _wavePauseTimer -= delta;
        if (_wavePauseTimer > 0f)
        {
            return;
        }

        _wave++;
        _waveBannerTimer = 2.5f;
        int count = Math.Min(3 + 2 * _wave, 18);
        for (int i = 0; i < count; i++)
        {
            SpawnEnemy(RandomEdgePosition(9f));
        }
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
            {
                return position;
            }
        }
        return new Vector3(BoundsHalf, BoundsHalf, 0.6f);
    }

    private void SpawnEnemy(Vector3 position)
    {
        var body = new SceneObject
        {
            Mesh = _cubeMesh,
            Static = false,
            BaseColor = new Vector3(0.42f, 0.08f, 0.06f),
            Metallic = 0.7f,
            Roughness = 0.35f,
            Emissive = new Vector3(4.0f, 0.5f, 0.12f),
            Transform = new Transform3D(position, Quaternion.Identity, new Vector3(1.0f)),
        };
        _gbufferRenderer.Add(body);
        _shadowRenderer.Add(body);
        _enemies.Add(new Enemy
        {
            Position = position,
            PreviousPosition = position,
            Body = body,
            Speed = MathF.Min(3.4f + 0.15f * _wave, 6f),
            Phase = _random.NextFloat(0f, MathF.PI * 2f),
        });
    }

    /// <summary>Explosion light plus a smoke puff; the drone's body unregisters.</summary>
    private void KillEnemy(Enemy enemy)
    {
        _gbufferRenderer.Remove(enemy.Body);
        _shadowRenderer.Remove(enemy.Body);
        _enemies.Remove(enemy);

        _explosions.Add((enemy.Position, 0f));
        if (_trails.TryCreateInstance(_puffTrailEffect, enemy.Position, out TrailEffectInstance3D puff))
        {
            _puffs.Add(new Puff { Trail = puff, Position = enemy.Position, EmitTimer = 0.18f });
        }
    }

    /// <summary>
    /// Death puffs: the head jitters around the blast point for a moment so the
    /// spacing emitter lays a tight cluster of points, then finishes; the smoke
    /// surface's buoyancy and erosion do the rest.
    /// </summary>
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
                {
                    puff.Trail.Finish(puff.Position);
                }
            }
            if (!puff.Trail.IsAlive)
            {
                _puffs.RemoveAt(i);
            }
        }
    }

    private void OnPlayerDeath()
    {
        _explosions.Add((_playerPosition, 0f));
        _bestScore = Math.Max(_bestScore, _score);
        _score = 0;
        _wave = 0;
        _wavePauseTimer = 2.5f;
        _playerHp = MaxHp;
        _playerPosition = new Vector3(0f, 0f, PlayerZ);
        _invulnerabilityTimer = 2f;
        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            KillEnemy(_enemies[i]);
        }
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            _bullets[i].Trail?.Finish(_bullets[i].Position);
            RemoveBullet(i);
        }
    }

    /// <summary>The scripted demo behind --screenshot (tick-keyed, so it is
    /// deterministic at any render rate): a converging ring of drones and an
    /// auto-firing ship, so the capture shows tracers, smoke and blasts.</summary>
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
        // Aim at the nearest drone (the fire cadence lives in TickShooting).
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
            {
                _aimDirection = Vector2.Normalize(toEnemy);
            }
        }
    }

    /// <summary>Lamps, the decaying muzzle flash and the live explosion pool.</summary>
    private void UploadPointLights(float delta)
    {
        int count = 0;
        Vector3 lampColor = new(0.35f, 0.75f, 1.0f);
        for (int i = 0; i < _lampPositions.Length; i++)
        {
            _pointLights[count++] = new PBRSceneEnvironment.PointLight(_lampPositions[i], lampColor, 3.2f, 14f);
        }
        if (_muzzleFlash > 0f)
        {
            _pointLights[count++] = new PBRSceneEnvironment.PointLight(
                _lastMuzzle, new Vector3(1f, 0.8f, 0.45f), 6f * _muzzleFlash, 7f);
        }
        // A soft cyan hero light above the ship keeps it readable on the dark floor.
        _pointLights[count++] = new PBRSceneEnvironment.PointLight(
            _playerPosition + new Vector3(0, 0, 2.6f), new Vector3(0.5f, 0.8f, 1f), 2.2f, 8f);
        for (int i = _explosions.Count - 1; i >= 0; i--)
        {
            (Vector3 position, float timer) = _explosions[i];
            timer += delta;
            float fade = 1f - timer / 0.45f;
            if (fade <= 0f)
            {
                _explosions.RemoveAt(i);
                continue;
            }
            _explosions[i] = (position, timer);
            if (count < _pointLights.Length)
            {
                _pointLights[count++] = new PBRSceneEnvironment.PointLight(
                    position + new Vector3(0, 0, 0.6f), new Vector3(1f, 0.5f, 0.22f), 12f * fade * fade, 11f);
            }
        }
        _environment.UpdatePointLights(_pointLights.AsSpan(0, count));
    }

    /// <summary>Pushes a mover's XY circle out of any prop it overlaps in z.</summary>
    private void PushOutOfColliders(ref Vector3 position, float radius)
    {
        foreach (Box box in _colliders)
        {
            if (position.Z + radius * 0.5f <= box.Min.Z || position.Z - radius * 0.5f >= box.Max.Z)
            {
                continue;
            }
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

    /// <summary>The crosshair, health bar and status line, drawn onto the foreground draw list.</summary>
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

        // Health bar, bottom-left.
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

    protected override void OnStop()
    {
        _trails.Dispose();
        _gbufferRenderer.Dispose();
        _shadowRenderer.Dispose();
        _preset.Dispose();
        _materialCompiler.Dispose();
    }

    /// <summary>Slab intersection of the segment from->to with an AABB; t in [0, 1].</summary>
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
                {
                    return false;
                }
                continue;
            }
            float inv = 1f / direction;
            float t1 = (min - origin) * inv;
            float t2 = (max - origin) * inv;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }
            t = MathF.Max(t, t1);
            tMax = MathF.Min(tMax, t2);
            if (t > tMax)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>24-vertex unit cube (outward normals, CCW winding) in the VertexPBR layout.</summary>
    private PrimitiveMesh CreateCubeMesh()
    {
        Span<VertexPBR> vertices = stackalloc VertexPBR[24];
        Span<ushort> indices = stackalloc ushort[36];

        // face data: normal, tangent axis a, tangent axis b (a x b == normal)
        Span<Vector3> normals = stackalloc Vector3[]
        {
            new(0, 0, 1), new(0, 0, -1),
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
        };
        Span<Vector3> aAxes = stackalloc Vector3[]
        {
            new(1, 0, 0), new(1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        };
        Span<Vector3> bAxes = stackalloc Vector3[]
        {
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, 1),
            new(1, 0, 0), new(1, 0, 0),
        };

        int vertexIndex = 0;
        int indexIndex = 0;
        Span<Vector2> uvs = stackalloc Vector2[]
        {
            new(0, 0), new(1, 0), new(1, 1), new(0, 1),
        };
        Span<Vector3> corners = stackalloc Vector3[4];
        for (int face = 0; face < 6; face++)
        {
            Vector3 normal = normals[face];
            Vector3 a = aAxes[face];
            Vector3 b = bAxes[face];
            Vector3 center = normal * 0.5f;
            Vector3 aHalf = a * 0.5f;
            Vector3 bHalf = b * 0.5f;

            corners[0] = center - aHalf - bHalf;
            corners[1] = center + aHalf - bHalf;
            corners[2] = center + aHalf + bHalf;
            corners[3] = center - aHalf + bHalf;

            for (int i = 0; i < 4; i++)
            {
                vertices[vertexIndex] = new VertexPBR(corners[i], normal, uvs[i], new Vector4(a, 1));
                vertexIndex++;
            }

            indices[indexIndex++] = (ushort)(face * 4 + 0);
            indices[indexIndex++] = (ushort)(face * 4 + 1);
            indices[indexIndex++] = (ushort)(face * 4 + 2);
            indices[indexIndex++] = (ushort)(face * 4 + 0);
            indices[indexIndex++] = (ushort)(face * 4 + 2);
            indices[indexIndex++] = (ushort)(face * 4 + 3);
        }

        return RenderingSystem.CreatePrimitiveMesh(vertices, indices, "cube");
    }

    /// <summary>A flat ground plane in the VertexPBR layout, UVs tiled for the checker.</summary>
    private PrimitiveMesh CreateGroundMesh(float size, int segments, float uvTiles)
    {
        int vertexCount = (segments + 1) * (segments + 1);
        int indexCount = segments * segments * 6;
        VertexPBR[] vertices = new VertexPBR[vertexCount];
        ushort[] indices = new ushort[indexCount];

        int vertexIndex = 0;
        for (int j = 0; j <= segments; j++)
        {
            for (int i = 0; i <= segments; i++)
            {
                float x = (float)i / segments * size - size * 0.5f;
                float y = (float)j / segments * size - size * 0.5f;
                vertices[vertexIndex++] = new VertexPBR(
                    new Vector3(x, y, 0),
                    Vector3.UnitZ,
                    new Vector2((float)i / segments * uvTiles, (float)j / segments * uvTiles));
            }
        }

        int indexIndex = 0;
        for (int j = 0; j < segments; j++)
        {
            for (int i = 0; i < segments; i++)
            {
                int i0 = j * (segments + 1) + i;
                int i1 = i0 + 1;
                int i2 = i0 + (segments + 1);
                int i3 = i2 + 1;
                // CCW when viewed from +Z.
                indices[indexIndex++] = (ushort)i0;
                indices[indexIndex++] = (ushort)i1;
                indices[indexIndex++] = (ushort)i2;
                indices[indexIndex++] = (ushort)i1;
                indices[indexIndex++] = (ushort)i3;
                indices[indexIndex++] = (ushort)i2;
            }
        }

        return RenderingSystem.CreatePrimitiveMesh(vertices, indices, "ground");
    }

    private Texture2D CreateCheckerTexture(int size)
    {
        const int tileSize = 16;
        byte[] data = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool light = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                byte value = light ? (byte)175 : (byte)150;
                int index = (y * size + x) * 4;
                data[index] = value;
                data[index + 1] = value;
                data[index + 2] = value;
                data[index + 3] = 255;
            }
        }
        return RenderingSystem.CreateTexture2D(data, (uint)size, (uint)size,
            new ImageLoadOption(format: PixelFormat.RGBA8UnormSrgb, addressMode: AddressMode.Repeat, filterMode: FilterMode.Linear, name: "checker_albedo"));
    }

    // Build a quaternion so that Transform(UnitX, q) == forward and the up axis
    // stays as close to worldUp as the forward direction allows (+X-forward / +Z-up).
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
            {
                return arg[prefix.Length..];
            }
        }
        return null;
    }

    /// <summary>The trail node: the bullet ribbons and death puffs, alpha-blended
    /// into the HDR scene color after deferred lighting (depth-tested against the
    /// shared G-buffer depth), ahead of bloom and tonemapping.</summary>
    private sealed class TrailNode : RGNode_SceneContent
    {
        private readonly GpuTrailSystem3D _trails;

        public TrailNode(GpuTrailSystem3D trails, RenderGraph graph, RenderChain chain) : base(graph, chain)
        {
            _trails = trails;
        }

        protected override void OnRender(in RenderGraphContext context, GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            using (RenderPassScope pass = context.RenderContext.BeginPass(target))
            {
                _trails.Render(pass);
            }
        }
    }
}
