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
/// The scene shows a looping flame jet, one-shot explosions (sparks + smoke groups)
/// re-spawning constantly (the frequent create/destroy path) and a vortex whose
/// simulation comes from a custom slang behavior module (SbVortex3D, local
/// simulation space).
/// <br/>Controls: hold the middle mouse button to orbit, ESC to exit.
/// <br/>CLI: --screenshot=&lt;path.png&gt; [--frames=N]
/// </summary>
public class Game : GameEngine
{
    private readonly RenderPipeline _pipeline;
    private readonly CameraPerspectiveBuffer _camera;
    private readonly GpuParticleSystem3D _particles;
    private readonly Dictionary<string, ParticleEffect3DAsset> _effects = new();
    private readonly List<ParticleEffectInstance3D> _instances = new();
    private FastRandom _random = new(54321);

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

    // Screenshot mode.
    private readonly string? _screenshotPath;
    private readonly int _screenshotFrames;
    private int _frameCount;
    private RGNode_Capture? _screenshotCaptureNode;
    private PngReadbackPipeline? _screenshotReadback;
    private bool _screenshotArmed;

    public Game(GameEngineSetting setting, string[] args) : base(setting)
    {
        _screenshotPath = GetArgValue(args, "--screenshot=");
        _screenshotFrames = int.TryParse(GetArgValue(args, "--frames="), out int frames) ? frames : 90;

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

        // The static scene (deterministic seeds keep screenshot mode reproducible).
        Spawn("Flare", new Vector3(-6, -3, 0), 201);
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

    private ParticleEffectInstance3D Spawn(string effectName, Vector3 position, int seed)
    {
        Transform3D transform = Transform3D.Identity;
        transform.Position = position;
        var instance = _particles.CreateInstance(_effects[effectName], transform, seed);
        _instances.Add(instance);
        return instance;
    }

    private ParticleEffectInstance3D SpawnRandom(string effectName)
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
            if (!_instances[i].IsActive)
            {
                _instances[i].Dispose();
                _instances.RemoveAt(i);
            }
        }

        DrawImGuiPanel();

        DebugStats.Text(FrameRate);

        _frameCount++;
        if (_screenshotPath != null && !_screenshotArmed && _frameCount >= _screenshotFrames)
        {
            ArmScreenshot(_screenshotPath);
        }

        _pipeline.Render(MainPresenter.FrameBuffer);

        PollScreenshot();
    }

    private void UpdateCamera()
    {
        bool rotating = MainView.IsFocused && Input.IsMousePressing(Mouse.Middle);
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
        ImGui.Begin("GPU Particles 3D");

        ImGui.Text($"Instances: {_instances.Count}");
        int groups = 0;
        foreach (ParticleEffectInstance3D instance in _instances)
        {
            groups += instance.Asset.Groups.Count;
        }
        ImGui.Text($"Emitter groups: {groups}");

        if (ImGui.Button("Spawn Explosion")) SpawnRandom("Explosion");
        if (ImGui.Button("Spawn Flare")) SpawnRandom("Flare");
        if (ImGui.Button("Spawn Vortex")) SpawnRandom("Vortex");

        if (ImGui.Button("Destroy Oldest") && _instances.Count > 0)
        {
            _instances[0].Dispose();
            _instances.RemoveAt(0);
        }
        if (ImGui.Button("Destroy All"))
        {
            foreach (ParticleEffectInstance3D instance in _instances)
            {
                instance.Dispose();
            }
            _instances.Clear();
        }

        ImGui.Checkbox("Auto Spawn Explosions", ref _autoSpawn);
        ImGui.SliderFloat("Spawn Interval", ref _autoSpawnInterval, 0.05f, 3.0f);

        ImGui.End();
    }

    /// <summary>Arms the chain-tail screenshot (see Sandbox 34-PBRDeferred).</summary>
    private void ArmScreenshot(string path)
    {
        _screenshotArmed = true;
        _screenshotCaptureNode = new RGNode_Capture(
            RenderingSystem,
            _pipeline.Graph,
            _pipeline.Chain,
            _pipeline.BlitShader);
        _pipeline.Use(_screenshotCaptureNode);
        _screenshotReadback = new PngReadbackPipeline(GraphicsDevice);
        _screenshotCaptureNode.Submit();
    }

    /// <summary>Pumps the armed screenshot and stops the sandbox once saved.</summary>
    private void PollScreenshot()
    {
        if (!_screenshotArmed)
        {
            return;
        }

        if (_screenshotCaptureNode!.TryTakeCompleted())
        {
            if (!_screenshotReadback!.TryBeginRead(_screenshotCaptureNode.CaptureTexture, out RenderCaptureResult? beginFailure))
            {
                Console.WriteLine($"Screenshot failed: {beginFailure!.Error}");
                Stop();
                return;
            }
        }

        RenderCaptureResult? result = _screenshotReadback!.Poll();
        if (result == null)
        {
            return;
        }

        if (result.Success && result.PngBytes != null)
        {
            File.WriteAllBytes(_screenshotPath!, result.PngBytes);
            Console.WriteLine($"Screenshot saved to {_screenshotPath}");
        }
        else
        {
            Console.WriteLine($"Screenshot failed: {result.Error}");
        }

        Stop();
    }

    protected override void OnStop()
    {
        _screenshotReadback?.Dispose();
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
}
