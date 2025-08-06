using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;
using Alco.ImGUI;

/// <summary>
/// Demonstrates batched rendering of multiple small particle systems
/// </summary>
public class Game : GameEngine
{
    private readonly Camera2DBuffer _camera;
    private readonly Material _materialParticle;
    private readonly List<ParticleSystem2DCPU> _particleSystems;
    private readonly List<ParticleEmitterBox2D> _emitters;
    private readonly List<ParticleSimulatorColorLerp2D> _simulators;
    private readonly Mesh _mesh;

    private RenderContext _renderContext;
    private SubRenderContext _subRenderContext;
    private InstanceRenderer<ParticleData2D> _renderer;

    private Texture2D _particleTexture;
    
    // Configuration
    private const int ParticleSystemCount = 16; // 4x4 grid
    private const int ParticlesPerSystem = 500;
    private const float GridSpacing = 20f;
    
    // Predefined bright colors that are easily visible on black background
    private static readonly Vector4[] BrightColors = 
    {
        new Vector4(1.0f, 0.2f, 0.2f, 1.0f), // Bright Red
        new Vector4(0.2f, 1.0f, 0.2f, 1.0f), // Bright Green
        new Vector4(0.2f, 0.2f, 1.0f, 1.0f), // Bright Blue
        new Vector4(1.0f, 1.0f, 0.2f, 1.0f), // Bright Yellow
        new Vector4(1.0f, 0.2f, 1.0f, 1.0f), // Bright Magenta
        new Vector4(0.2f, 1.0f, 1.0f, 1.0f), // Bright Cyan
        new Vector4(1.0f, 0.6f, 0.2f, 1.0f), // Bright Orange
        new Vector4(0.6f, 0.2f, 1.0f, 1.0f), // Bright Purple
        new Vector4(1.0f, 0.8f, 0.8f, 1.0f), // Light Pink
        new Vector4(0.8f, 1.0f, 0.8f, 1.0f), // Light Green
        new Vector4(0.8f, 0.8f, 1.0f, 1.0f), // Light Blue
        new Vector4(1.0f, 1.0f, 0.8f, 1.0f), // Light Yellow
        new Vector4(1.0f, 0.4f, 0.6f, 1.0f), // Rose
        new Vector4(0.4f, 1.0f, 0.6f, 1.0f), // Lime
        new Vector4(0.6f, 0.4f, 1.0f, 1.0f), // Lavender
        new Vector4(1.0f, 0.7f, 0.4f, 1.0f), // Peach
    };
    
    // Performance tracking
    private int _totalParticleCount = 0;
    private bool _showPerformanceStats = true;
    
    // UI state variables
    private float _globalEmissionMultiplier = 1.0f;
    private float _globalLifetimeMultiplier = 1.0f;
    private bool _allPlaying = true;

    public Game(GameEngineSetting setting) : base(setting)
    {
        // Create camera with larger view for multiple systems
        _camera = RenderingSystem.CreateCamera2D(128, 72, 100);

        // Create material for particles
        _materialParticle = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_Particle2D);
        _materialParticle.BlendState = BlendState.Additive;
        _materialParticle.SetBuffer(ShaderResourceId.Camera, _camera);

        // Load particle texture
        _particleTexture = AssetSystem.Load<Texture2D>("Droplet");
        _materialParticle.SetTexture(ShaderResourceId.Texture, _particleTexture);

        // Create render context
        _renderContext = RenderingSystem.CreateRenderContext();
        _subRenderContext = RenderingSystem.CreateSubRenderContext();

        _mesh = RenderingSystem.MeshCenteredSprite;

        // Create instance renderer with larger buffer for multiple systems
        _renderer = RenderingSystem.CreateInstanceRenderer<ParticleData2D>(
            _subRenderContext, 
            _materialParticle, 
            "_particles", 
            256 * 1024, // 2MB buffer for better batching
            "ParticleBatchingRenderer"
        );

        // Initialize collections
        _particleSystems = new List<ParticleSystem2DCPU>();
        _emitters = new List<ParticleEmitterBox2D>();
        _simulators = new List<ParticleSimulatorColorLerp2D>();

        CreateParticleSystems();
    }

    /// <summary>
    /// Creates multiple small particle systems in a grid layout
    /// </summary>
    private void CreateParticleSystems()
    {
        int gridSize = (int)Math.Sqrt(ParticleSystemCount);
        
        for (int i = 0; i < ParticleSystemCount; i++)
        {
            int x = i % gridSize;
            int y = i / gridSize;
            
            // Calculate position in grid
            Vector2 position = new Vector2(
                (x - gridSize / 2f) * GridSpacing,
                (y - gridSize / 2f) * GridSpacing
            );

            // Create emitter with random properties
            var emitter = new ParticleEmitterBox2D(position, new Vector2(1, 1));
            emitter.MinSpeed = Random.Shared.NextSingle() * 5f + 2f;
            emitter.MaxSpeed = emitter.MinSpeed + Random.Shared.NextSingle() * 5f;
            emitter.MinSize = emitter.MaxSize = Random.Shared.NextSingle() * 1.5f + 0.5f;
            emitter.MinRotation = Random.Shared.NextSingle() * 360f;
            emitter.MaxRotation = emitter.MinRotation + Random.Shared.NextSingle() * 60f;
            emitter.ConeAngle = Random.Shared.NextSingle() * 45f + 15f;
            
            // Use bright, predefined colors that are easily visible on black background
            emitter.Color = BrightColors[i % BrightColors.Length];

            _emitters.Add(emitter);

            // Create simulator with varying colors
            var simulator = new ParticleSimulatorColorLerp2D();
            simulator.StartColor = emitter.Color;
            simulator.EndColor = new Vector4(emitter.Color.R, emitter.Color.G, emitter.Color.B, 0f); // Fade to transparent
            
            _simulators.Add(simulator);

            // Create particle system
            var particleSystem = new ParticleSystem2DCPU(emitter, simulator)
            {
                EmissionRateOverTime = Random.Shared.NextSingle() * 100f + 50f,
                ParticleLifetime = Random.Shared.NextSingle() * 2f + 1f,
                MaxParticles = ParticlesPerSystem
            };
            
            particleSystem.Play();
            _particleSystems.Add(particleSystem);
        }
    }

    protected override void OnTick(float delta)
    {
        // Simulate all particle systems
        // Batch render all particles using EnqueueInstances
        _subRenderContext.Begin(MainFrameBuffer.AttachmentLayout);
        
        // Enqueue particles from each system - the InstanceRenderer will batch them automatically
        foreach (var system in _particleSystems)
        {
            system.Simulate(delta);
            _renderer.EnqueueInstances(system.Particles);
        }


        _renderer.Draw(_mesh);
        

        _subRenderContext.End();
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        // Render particles
        if (_subRenderContext.HasBuffer)
        {
            _renderContext.Begin(MainFrameBuffer);
            _renderContext.ExecuteSubContext(_subRenderContext);
            _renderContext.End();
        }

        // Show performance and controls
        if (_showPerformanceStats)
        {
            ShowPerformanceStats();
        }

        ShowControls();
    }

    /// <summary>
    /// Shows performance statistics
    /// </summary>
    private void ShowPerformanceStats()
    {
        ImGui.Begin("Performance Stats");

        FixedString8 strFramerate = new FixedString8();
        strFramerate.Append(FrameRate);

        ImGui.Text($"FPS: {strFramerate}");
        ImGui.Text($"Active Particle Systems: {_particleSystems.Count}");
        ImGui.Text($"Total Particles: {_totalParticleCount}");
        ImGui.Text($"Particles per System: ~{_totalParticleCount / Math.Max(1, _particleSystems.Count)}");
        
        ImGui.Separator();
        ImGui.Text("Batching Benefits:");
        ImGui.Text($"• EnqueueInstances from {_particleSystems.Count} systems");
        ImGui.Text($"• Automatic DrawData merging optimization");
        ImGui.Text($"• Single Draw() call renders all particles");
        ImGui.Text($"• Minimized GPU state changes");
        
        ImGui.End();
    }

    /// <summary>
    /// Shows control panel
    /// </summary>
    private void ShowControls()
    {
        ImGui.Begin("Particle Batching Control");

        ImGui.Checkbox("Show Performance Stats", ref _showPerformanceStats);

        ImGui.Separator();
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Global Controls");

        // Global emission rate multiplier
        if (ImGui.SliderFloat("Global Emission Multiplier", ref _globalEmissionMultiplier, 0.1f, 3.0f))
        {
            foreach (var system in _particleSystems)
            {
                // Restore base emission rate and apply multiplier
                var baseRate = 75f; // Average base rate
                system.EmissionRateOverTime = baseRate * _globalEmissionMultiplier;
            }
        }

        // Global lifetime multiplier
        if (ImGui.SliderFloat("Global Lifetime Multiplier", ref _globalLifetimeMultiplier, 0.1f, 3.0f))
        {
            foreach (var system in _particleSystems)
            {
                var baseLifetime = 1.5f; // Average base lifetime
                system.ParticleLifetime = baseLifetime * _globalLifetimeMultiplier;
            }
        }

        ImGui.Separator();

        // Reset button
        if (ImGui.Button("Reset All Systems"))
        {
            ResetAllSystems();
        }

        ImGui.SameLine();
        
        // Play/Stop all
        if (ImGui.Button(_allPlaying ? "Stop All" : "Play All"))
        {
            _allPlaying = !_allPlaying;
            foreach (var system in _particleSystems)
            {
                if (_allPlaying)
                    system.Play();
                else
                    system.Stop();
            }
        }

        // Camera controls
        ImGui.Separator();
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Camera");
        
        var cameraPos = _camera.Position;
        if (ImGui.SliderFloat2("Camera Position", ref cameraPos, -50f, 50f))
        {
            _camera.Position = cameraPos;
        }

        var zoom = _camera.Width;
        if (ImGui.SliderFloat("Camera Zoom", ref zoom, 32f, 256f))
        {
            _camera.ViewSize = new Vector2(zoom, zoom * 9f / 16f);
        }

        ImGui.End();
    }

    /// <summary>
    /// Resets all particle systems
    /// </summary>
    private void ResetAllSystems()
    {
        foreach (var system in _particleSystems)
        {
            system.Stop();
            system.Play();
        }
    }

    protected override void OnStop()
    {
        _renderContext.Dispose();
    }
}