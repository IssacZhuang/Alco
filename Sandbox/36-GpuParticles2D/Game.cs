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
/// instanced draw per group per frame). The scene shows six effects — a one-shot
/// explosion (two groups: sparks + smoke, the sparks velocity-stretched), a looping
/// flame (two groups), a looping fountain with a periodic burst, a vortex whose
/// simulation comes from a custom slang behavior module (SbVortex2D, local
/// simulation space), a dissolve whose visuals come from a material asset
/// (Materials/Dissolve2D.amat: a custom slang surface with a shared noise texture
/// and uniform parameters; the group derives only its sprite over the material's
/// "texture" slot), and a rainbow jet whose colors and growth come from a baked
/// color gradient and size curve (the over-life lookup textures).
/// <br/>The auto-spawner exercises the frequent create/destroy path: explosions
/// spawn at random positions and finished instances dispose themselves, returning
/// their pool slices.
/// <br/>The ImGui panel manages the live instances: a click-selectable instance
/// list (dead instances are pruned automatically), per-effect spawn buttons
/// (spawned at the screen center), delete, and — for the selected instance — a
/// scene gizmo (translate in the view plane / rotate around the view axis) plus
/// live per-group parameter editing (emission rate, lifetime, speed, size,
/// gravity, tint, velocity stretch) through the instance's params override API.
/// <br/>CLI: --screenshot=&lt;path.png&gt; [--frames=N]
/// </summary>
public class Game : GameEngine
{
    private readonly RenderPipeline _pipeline;
    private readonly Camera2DBuffer _camera;
    private readonly GpuParticleSystem2D _particles;
    private readonly Dictionary<string, ParticleEffect2DAsset> _effects = new();
    private readonly List<string> _effectNames = [];
    private readonly List<InstanceEntry> _instances = [];
    private FastRandom _random = new(12345);

    // The selected instance (panel selection + gizmo target); null when none.
    private InstanceEntry? _selected;
    private int _nextInstanceId = 1;

    // The gizmo operation of the selected instance: plane translate / view-axis rotate.
    private GizmoOperation _gizmoOperation = GizmoOperation.TranslateXY;

    // Auto spawner (frequent create/destroy exercise).
    private bool _autoSpawn = true;
    private float _autoSpawnInterval = 0.8f;
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

    // Stress mode (--stress=N): a grid of N flame/vortex instances plus rapid-fire
    // explosions, with the frame rate logged to the console once per second.
    private readonly int _stressCount;
    private float _stressLogTimer;

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

        _camera = RenderingSystem.CreateCamera2D(64, 36, 100);
        Gizmo.IsOrthographic = true;

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
        _effects["Rainbow"] = AssetSystem.Load<ParticleEffect2DAsset>("Effects/Rainbow.apeff");
        _effectNames.AddRange(_effects.Keys);

        // The static scene (deterministic seeds keep screenshot mode reproducible).
        // The flame starts selected: it doubles as the gizmo showcase (its
        // world-space particles leave a trail when it moves). Its spot sits clear
        // of the panel's default window rectangle so the gizmo stays visible.
        _selected = Spawn("Flame", new Vector2(6, -4), 101);
        Spawn("Fountain", new Vector2(18, -15), 102);
        Spawn("Vortex", new Vector2(0, 5), 103);
        Spawn("Shockwave", new Vector2(24, 8), 104);
        Spawn("Dissolve", new Vector2(-26, 8), 105);
        Spawn("Rainbow", new Vector2(-2, -13), 106);

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

    private InstanceEntry Spawn(string effectName, Vector2 position, int seed)
    {
        ParticleEffectInstance2D instance = _particles.CreateInstance(_effects[effectName], new Transform2D(position), seed);
        var entry = new InstanceEntry(instance, _nextInstanceId++);
        _instances.Add(entry);
        return entry;
    }

    // Spawns at (roughly) the screen center so the new effect is immediately
    // visible — the manual spawn buttons use this, unlike the ambient
    // auto-spawner which keeps scattering explosions randomly.
    private InstanceEntry SpawnVisible(string effectName)
    {
        Vector2 position = new(_random.NextFloat(-3, 3), _random.NextFloat(-3, 3));
        return Spawn(effectName, position, (int)_random.NextUint());
    }

    private InstanceEntry SpawnRandom(string effectName)
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

        if (_stressCount > 0)
        {
            _stressLogTimer += delta;
            if (_stressLogTimer >= 1.0f)
            {
                _stressLogTimer = 0;
                long particles = 0;
                foreach (InstanceEntry entry in _instances)
                {
                    foreach (ParticleGroup2DAsset group in entry.Instance.Asset.Groups)
                    {
                        particles += group.MaxParticles;
                    }
                }
                Console.WriteLine($"[stress] fps={FrameRate} instances={_instances.Count} pooledParticles={particles}");
            }
        }

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

    private void DrawImGuiPanel()
    {
        // Two columns, the editor's BeginChild + SameLine idiom: the instance list
        // with spawn/delete on the left, the selected instance's parameter editor
        // on the right. 'Appearing' (not 'FirstUseEver') so a stale imgui.ini
        // cannot shrink the window.
        ImGui.SetNextWindowSize(new Vector2(680, 700), ImGuiCond.Appearing);
        ImGui.Begin("GPU Particles 2D");

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

            // Manual spawns land at the screen center so they are immediately visible.
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
                ParticleEffectInstance2D instance = _selected.Instance;
                ImGui.SeparatorText($"Selected: {instance.Asset.Name} #{_selected.Id}");

                ImGui.Text("Gizmo:");
                ImGui.SameLine();
                if (ImGui.RadioButton("Translate", _gizmoOperation == GizmoOperation.TranslateXY))
                {
                    _gizmoOperation = GizmoOperation.TranslateXY;
                }
                ImGui.SameLine();
                if (ImGui.RadioButton("Rotate", _gizmoOperation == GizmoOperation.RotateZ))
                {
                    _gizmoOperation = GizmoOperation.RotateZ;
                }

                Transform2D transform = instance.Transform;
                if (ImGui.EditTransform2D(ref transform))
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
    /// The selection gizmo: translate in the view plane or rotate around the view
    /// axis, depending on the mode chosen in the panel. Drawn outside the panel
    /// window so the handles are not clipped to it (the pattern of the
    /// 14-Collision / 27-Particles / 28-Gizmo sandboxes).
    /// </summary>
    private void DrawSelectionGizmo()
    {
        if (_selected == null)
        {
            return;
        }
        Transform2D transform = _selected.Instance.Transform;
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
    /// the edited one back through <see cref="ParticleEffectInstance2D.SetGroupParams"/>,
    /// which applies to the running instance without respawning it.
    /// </summary>
    private static void DrawGroupEditor(ParticleEffectInstance2D instance, int groupIndex)
    {
        ImGui.PushID(groupIndex);
        EmitterParams2D parameters = instance.GetGroupParams(groupIndex);
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

        Vector2 sizeMin = new(parameters.Size.X, parameters.Size.Y);
        Vector2 sizeMax = new(parameters.Size.Z, parameters.Size.W);
        if (ImGui.DragFloat2("Size Min", ref sizeMin, 0.02f, 0.01f, 50f))
        {
            sizeMin = Vector2.Min(sizeMin, sizeMax);
            edited = true;
        }
        if (ImGui.DragFloat2("Size Max", ref sizeMax, 0.02f, 0.01f, 50f))
        {
            sizeMax = Vector2.Max(sizeMin, sizeMax);
            edited = true;
        }
        parameters.Size = new Vector4(sizeMin.X, sizeMin.Y, sizeMax.X, sizeMax.Y);

        // Gravity as a scale of the authored value (keeps its direction); groups
        // authored without gravity edit the vector directly.
        Vector2 assetGravity = instance.Asset.Groups[groupIndex].Gravity;
        Vector2 gravity = new(parameters.Motion.X, parameters.Motion.Y);
        if (assetGravity.LengthSquared() > 1e-10f)
        {
            float gravityScale = gravity.Length() / assetGravity.Length();
            if (ImGui.DragFloat("Gravity Scale", ref gravityScale, 0.02f, -10f, 10f))
            {
                Vector2 scaled = assetGravity * gravityScale;
                parameters.Motion = new Vector4(scaled, parameters.Motion.Z, parameters.Motion.W);
                edited = true;
            }
        }
        else if (ImGui.DragFloat2("Gravity", ref gravity, 0.05f))
        {
            parameters.Motion = new Vector4(gravity, parameters.Motion.Z, parameters.Motion.W);
            edited = true;
        }

        ColorFloat tint = parameters.Tint;
        if (ImGui.ColorEdit4("Tint", ref tint))
        {
            parameters.Tint = tint;
            edited = true;
        }

        // Velocity stretch (2D: only meaningful with align-rotation-to-velocity).
        bool alignToVelocity = parameters.Speed.Z > 0.5f;
        bool stretch = (parameters.Flags & EmitterParams2D.FlagVelocityStretch) != 0u;
        ImGui.BeginDisabled(!alignToVelocity);
        if (ImGui.Checkbox("Velocity Stretch", ref stretch))
        {
            parameters.Flags = stretch
                ? parameters.Flags | EmitterParams2D.FlagVelocityStretch
                : parameters.Flags & ~EmitterParams2D.FlagVelocityStretch;
            edited = true;
        }
        ImGui.EndDisabled();
        if (!alignToVelocity)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(needs align-to-velocity)");
        }
        if (stretch)
        {
            float lengthScale = parameters.OverLife.Y;
            if (ImGui.DragFloat("Stretch Length Scale", ref lengthScale, 0.02f, 0f, 10f))
            {
                parameters.OverLife = new Vector4(parameters.OverLife.X, lengthScale, 0f, 0f);
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

    /// <summary>
    /// A live instance plus its stable list id — the instance list prunes and
    /// reorders, the id keeps the panel labels unique and recognizable.
    /// </summary>
    private sealed class InstanceEntry(ParticleEffectInstance2D instance, int id)
    {
        public readonly ParticleEffectInstance2D Instance = instance;
        public readonly int Id = id;
    }
}
