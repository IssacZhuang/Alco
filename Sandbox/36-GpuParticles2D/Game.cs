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
/// Sandbox demonstrating the 2D GPU particle system (Alco.Particles): particle
/// effects are assets (<c>.apeff</c>) with one or more emitter groups, simulated
/// and rendered entirely on the GPU (two compute dispatches + one indexed-indirect
/// instanced draw per group per frame). The scene shows five effects — a one-shot
/// explosion (two groups: sparks + smoke), a looping flame (two groups), a looping
/// fountain with a periodic burst, a vortex whose simulation comes from a custom
/// slang behavior module (SbVortex2D, local simulation space), and a dissolve
/// whose visuals come from a material asset (Materials/Dissolve2D.amat: a custom
/// slang surface with a shared noise texture and uniform parameters; the group
/// derives only its sprite over the material's "texture" slot).
/// <br/>The auto-spawner exercises the frequent create/destroy path: explosions
/// spawn at random positions and finished instances dispose themselves, returning
/// their pool slices.
/// <br/>CLI: --screenshot=&lt;path.png&gt; [--frames=N]
/// </summary>
public class Game : GameEngine
{
    private readonly RenderPipeline _pipeline;
    private readonly Camera2DBuffer _camera;
    private readonly GpuParticleSystem2D _particles;
    private readonly Dictionary<string, ParticleEffect2DAsset> _effects = new();
    private readonly List<ParticleEffectInstance2D> _instances = new();
    private FastRandom _random = new(12345);

    // The draggable flame instance (shows the world-space trail of a moving emitter).
    private ParticleEffectInstance2D? _flameInstance;

    // Auto spawner (frequent create/destroy exercise).
    private bool _autoSpawn = true;
    private float _autoSpawnInterval = 0.8f;
    private float _autoSpawnTimer;

    // Screenshot mode.
    private readonly string? _screenshotPath;
    private readonly int _screenshotFrames;
    private int _frameCount;
    private RGNode_Capture? _screenshotCaptureNode;
    private PngReadbackPipeline? _screenshotReadback;
    private bool _screenshotArmed;

    // Stress mode (--stress=N): a grid of N flame/vortex instances plus rapid-fire
    // explosions, with the frame rate logged to the console once per second.
    private readonly int _stressCount;
    private float _stressLogTimer;

    public Game(GameEngineSetting setting, string[] args) : base(setting)
    {
        _screenshotPath = GetArgValue(args, "--screenshot=");
        _screenshotFrames = int.TryParse(GetArgValue(args, "--frames="), out int frames) ? frames : 90;

        AddSystem(new ImGUISystem(this));

        _camera = RenderingSystem.CreateCamera2D(64, 36, 100);

        // The particle system and its asset loader; the shared pool starts large
        // enough for the stress scenario and grows geometrically when exhausted.
        _particles = new GpuParticleSystem2D(RenderingSystem, particleCapacity: 1 << 18, emitterSlots: 512)
        {
            Camera = _camera,
        };

        // The pipeline: clear + blit shell, then (in registration order) the
        // particle simulation, the scene content the particles draw into and the
        // HDR tonemap.
        _pipeline = new RenderPipeline(RenderingSystem, new RenderPipeline.Descriptor
        {
            SceneLayout = RenderingSystem.PreferredHDRPass,
            BlitShader = BuiltInAssets.Shader_Blit,
            Width = MainView.Size.X,
            Height = MainView.Size.Y,
            Name = "gpu_particles_2d",
        });
        _pipeline.Use(new RGNode_Callback { Callback = context => _particles.RecordSimulation(in context) });
        _pipeline.Use(new SceneNode(_particles, _pipeline.Graph, _pipeline.Chain));
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
        // Linear-to-sRGB: the ACES default gamma of 1 leaves the frame in linear space.
        ACESTonemapData acesData = tonemapNode.ACESData;
        acesData.Gamma = 2.2f;
        tonemapNode.ACESData = acesData;
        _pipeline.Use(tonemapNode);

        MainPresenter.OnResize += size => _pipeline.Resize(size.X, size.Y);

        // The effect assets.
        _effects["Explosion"] = AssetSystem.Load<ParticleEffect2DAsset>("Effects/Explosion.apeff");
        _effects["Flame"] = AssetSystem.Load<ParticleEffect2DAsset>("Effects/Flame.apeff");
        _effects["Fountain"] = AssetSystem.Load<ParticleEffect2DAsset>("Effects/Fountain.apeff");
        _effects["Vortex"] = AssetSystem.Load<ParticleEffect2DAsset>("Effects/Vortex.apeff");
        _effects["Shockwave"] = AssetSystem.Load<ParticleEffect2DAsset>("Effects/Shockwave2D.apeff");
        _effects["Dissolve"] = AssetSystem.Load<ParticleEffect2DAsset>("Effects/Dissolve2D.apeff");

        // The static scene (deterministic seeds keep screenshot mode reproducible).
        _flameInstance = Spawn("Flame", new Vector2(-18, -10), 101);
        Spawn("Fountain", new Vector2(18, -15), 102);
        Spawn("Vortex", new Vector2(0, 5), 103);
        Spawn("Shockwave", new Vector2(24, 8), 104);
        Spawn("Dissolve", new Vector2(-26, 8), 105);

        // Stress mode: a grid of alternating flame/vortex instances (~2300/4100
        // pooled particles each) and rapid-fire explosions on top.
        _stressCount = int.TryParse(GetArgValue(args, "--stress="), out int stress) ? stress : 0;
        if (_stressCount > 0)
        {
            int columns = (int)Math.Ceiling(Math.Sqrt(_stressCount * 2.0));
            for (int i = 0; i < _stressCount; i++)
            {
                float x = (i % columns - (columns - 1) * 0.5f) * 7.5f;
                float y = (i / columns) * 7.5f - 14f;
                Spawn(i % 2 == 0 ? "Flame" : "Vortex", new Vector2(x, y), 1000 + i);
            }
            _autoSpawnInterval = 0.1f;
        }
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

    private ParticleEffectInstance2D Spawn(string effectName, Vector2 position, int seed)
    {
        var instance = _particles.CreateInstance(_effects[effectName], new Transform2D(position), seed);
        _instances.Add(instance);
        return instance;
    }

    private ParticleEffectInstance2D SpawnRandom(string effectName)
    {
        Vector2 position = new(_random.NextFloat(-28, 28), _random.NextFloat(-15, 15));
        return Spawn(effectName, position, (int)_random.NextUint());
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        // The auto spawner: a steady stream of one-shot explosions; finished
        // (fully decayed) instances dispose themselves below.
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

        if (_stressCount > 0)
        {
            _stressLogTimer += delta;
            if (_stressLogTimer >= 1.0f)
            {
                _stressLogTimer = 0;
                long particles = 0;
                foreach (ParticleEffectInstance2D instance in _instances)
                {
                    foreach (ParticleGroup2DAsset group in instance.Asset.Groups)
                    {
                        particles += group.MaxParticles;
                    }
                }
                Console.WriteLine($"[stress] fps={FrameRate} instances={_instances.Count} pooledParticles={particles}");
            }
        }

        _frameCount++;
        if (_screenshotPath != null && !_screenshotArmed && _frameCount >= _screenshotFrames)
        {
            ArmScreenshot(_screenshotPath);
        }

        _pipeline.Render(MainPresenter.FrameBuffer);

        PollScreenshot();
    }

    private void DrawImGuiPanel()
    {
        ImGui.Begin("GPU Particles 2D");

        ImGui.Text($"Instances: {_instances.Count}");
        int groups = 0;
        foreach (ParticleEffectInstance2D instance in _instances)
        {
            groups += instance.Asset.Groups.Count;
        }
        ImGui.Text($"Emitter groups: {groups}");

        if (ImGui.Button("Spawn Explosion")) SpawnRandom("Explosion");
        if (ImGui.Button("Spawn Flame")) SpawnRandom("Flame");
        if (ImGui.Button("Spawn Fountain")) SpawnRandom("Fountain");
        if (ImGui.Button("Spawn Vortex")) SpawnRandom("Vortex");
        if (ImGui.Button("Spawn Dissolve")) SpawnRandom("Dissolve");

        if (ImGui.Button("Destroy Oldest") && _instances.Count > 0)
        {
            _instances[0].Dispose();
            _instances.RemoveAt(0);
        }
        if (ImGui.Button("Destroy All"))
        {
            foreach (ParticleEffectInstance2D instance in _instances)
            {
                instance.Dispose();
            }
            _instances.Clear();
            _flameInstance = null;
        }

        ImGui.Checkbox("Auto Spawn Explosions", ref _autoSpawn);
        ImGui.SliderFloat("Spawn Interval", ref _autoSpawnInterval, 0.05f, 3.0f);

        // Drag the flame emitter around; its world-space particles leave a trail.
        if (_flameInstance != null)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Flame Emitter (drag me)");
            Transform2D transform = _flameInstance.Transform;
            if (ImGui.EditTransform2D(ref transform))
            {
                _flameInstance.Transform = transform;
            }
        }

        ImGui.End();
    }

    /// <summary>
    /// Arms the chain-tail screenshot: an <see cref="RGNode_Capture"/> is inserted
    /// before the final blit and copies the fully post-processed frame into its
    /// RGBA8 texture; the readback and PNG encode complete on the following frames.
    /// </summary>
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

    /// <summary>The scene content node the particles draw into (in place, on the chain target).</summary>
    private sealed class SceneNode(GpuParticleSystem2D particles, RenderGraph graph, RenderChain chain)
        : RGNode_SceneContent(graph, chain)
    {
        protected override void OnRender(in RenderGraphContext context, GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            using (RenderPassScope pass = context.RenderContext.BeginPass(target))
            {
                particles.Render(pass);
            }
        }
    }
}
