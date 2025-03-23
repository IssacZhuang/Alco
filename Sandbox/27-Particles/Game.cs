using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;
using ImGuiNET;


public class Game : GameEngine
{
    private Camera2D _camera;
    private Material _materialParticle;
    private ParticleEmitterBox2D _emitter;
    private ParticleSimulatorColorLerp2D _simulator;
    private ParticleSystem2DCPU _particleSystem;
    
    private RenderContext _renderContext;
    private Texture2D _particleTexture;

    public Game(GameEngineSetting setting) : base(setting)
    {
        // Create camera
        _camera = Rendering.CreateCamera2D(64, 36, 100);
         
        // Create material for particles
        _materialParticle = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Particle2D);
        _materialParticle.BlendState = BlendState.Additive;
        _materialParticle.SetBuffer(ShaderResourceId.Camera, _camera);

        // Use default white texture if no specific texture is needed
        _particleTexture = Assets.Load<Texture2D>("Droplet");
        _materialParticle.SetTexture(ShaderResourceId.Texture, _particleTexture);

        // Create render context
        _renderContext = Rendering.CreateRenderContext();

        // Create particle emitter
        _emitter = new ParticleEmitterBox2D(Vector2.Zero, new Vector2(50, 10));
        _emitter.MinSpeed = 8.0f;
        _emitter.MaxSpeed = 15.0f;

        _emitter.MinRotation = 270f;
        _emitter.MaxRotation = 270f;

        _emitter.MinSize = _emitter.MaxSize = 2.5f;

        _simulator = new ParticleSimulatorColorLerp2D();

        // Create particle system
        _particleSystem = Rendering.CreateParticleSystem2DCPU(_materialParticle, _emitter, _simulator);
        _particleSystem.EmissionRateOverTime = 100;
        _particleSystem.ParticleLifetime = 1.0f;
        _particleSystem.MaxParticles = 100000;
        _particleSystem.Play();

    }

    protected override void OnTick(float delta)
    {
        // Simulate particles
        _particleSystem.Simulate(delta);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        // Draw particles
        _renderContext.Begin(MainFrameBuffer);
        _particleSystem.Render(_renderContext, Matrix4x4.Identity);
        _renderContext.End();

        // Show particle controls
        ImGui.Begin("Particle Control");

        FixedString8 strFramerate = new FixedString8();//zero allocation string
        strFramerate.Append(FrameRate);

        ImGui.Text(strFramerate);

        // Particle System Controls
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Particle System");

        float emissionRate = _particleSystem.EmissionRateOverTime;
        if (ImGui.SliderFloat("Emission Rate", ref emissionRate, 10, 100000))
        {
            _particleSystem.EmissionRateOverTime = emissionRate;
        }

        float lifetime = _particleSystem.ParticleLifetime;
        if (ImGui.SliderFloat("Lifetime", ref lifetime, 0.5f, 10.0f))
        {
            _particleSystem.ParticleLifetime = lifetime;
        }

        bool isBurst = _particleSystem.IsBurst;
        if (ImGui.Checkbox("Burst Mode", ref isBurst))
        {
            _particleSystem.IsBurst = isBurst;
        }

        if (_particleSystem.IsBurst)
        {
            if (ImGui.Button("Play Burst"))
            {
                _particleSystem.Play();
            }
        }

        int minBurstCount = _particleSystem.MinBurstCount;
        if (ImGui.SliderInt("Min Burst Count", ref minBurstCount, 1, 1000))
        {
            _particleSystem.MinBurstCount = minBurstCount;
        }

        int maxBurstCount = _particleSystem.MaxBurstCount;
        if (ImGui.SliderInt("Max Burst Count", ref maxBurstCount, 1, 1000))
        {
            _particleSystem.MaxBurstCount = maxBurstCount;
        }

        // Separator
        ImGui.Separator();

        // Particle Emitter Controls
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Particle Emitter");

        Vector2 position = _emitter.Position;
        if (ImGui.SliderFloat2("Position", ref position, -300, 300))
        {
            _emitter.Position = position;
        }

        Vector2 extents = _emitter.Extents;
        if (ImGui.SliderFloat2("Extents", ref extents, 1, 100))
        {
            _emitter.Extents = extents;
        }

        float minSpeed = _emitter.MinSpeed;
        if (ImGui.SliderFloat("Min Speed", ref minSpeed, 0, 100))
        {
            _emitter.MinSpeed = minSpeed;
            // Ensure MaxSpeed is never less than MinSpeed
            if (_emitter.MaxSpeed < minSpeed)
            {
                _emitter.MaxSpeed = minSpeed;
            }
        }

        float maxSpeed = _emitter.MaxSpeed;
        if (ImGui.SliderFloat("Max Speed", ref maxSpeed, 0, 200))
        {
            _emitter.MaxSpeed = maxSpeed;
            // Ensure MinSpeed is never more than MaxSpeed
            if (_emitter.MinSpeed > maxSpeed)
            {
                _emitter.MinSpeed = maxSpeed;
            }
        }

        float minSize = _emitter.MinSize;
        if (ImGui.SliderFloat("Min Size", ref minSize, 1, 100))
        {
            _emitter.MinSize = minSize;
        }

        float maxSize = _emitter.MaxSize;
        if (ImGui.SliderFloat("Max Size", ref maxSize, 1, 100))
        {
            _emitter.MaxSize = maxSize;
        }

        Vector4 color = _emitter.Color;
        if (ImGui.ColorEdit4("Color", ref color))
        {
            _emitter.Color = color;
        }

        bool isRotationFollowDirection = _emitter.IsRotationFollowDirection;
        if (ImGui.Checkbox("Rotation Follow Direction", ref isRotationFollowDirection))
        {
            _emitter.IsRotationFollowDirection = isRotationFollowDirection;
        }

        float minRotation = _emitter.MinRotation;
        if (ImGui.SliderFloat("Min Rotation", ref minRotation, 0, 360))
        {
            _emitter.MinRotation = minRotation;
        }

        float maxRotation = _emitter.MaxRotation;
        if (ImGui.SliderFloat("Max Rotation", ref maxRotation, 0, 360))
        {
            _emitter.MaxRotation = maxRotation;
        }
        
        

        //simulator
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Simulator");

        Vector4 startColor = _simulator.StartColor;
        if (ImGui.ColorEdit4("Start Color", ref startColor))
        {
            _simulator.StartColor = startColor;
        }

        Vector4 endColor = _simulator.EndColor;
        if (ImGui.ColorEdit4("End Color", ref endColor))
        {
            _simulator.EndColor = endColor;
        }
        
        
        

        ImGui.End();
    }

    protected override void OnStop()
    {
        _particleSystem.Dispose();
        _renderContext.Dispose();
    }
}