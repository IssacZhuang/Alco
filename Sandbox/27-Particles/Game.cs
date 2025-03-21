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
    private ParticleSystem2DCPU _particleSystem;
    private ParticleEmitterBox2D _emitter;
    private RenderContext _renderContext;
    private Texture2D _particleTexture;

    public Game(GameEngineSetting setting) : base(setting)
    {
        // Create camera
        _camera = Rendering.CreateCamera2D(64, 36, 100);

        // Create material for particles
        _materialParticle = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Particle2D);
        _materialParticle.BlendState = BlendState.AlphaBlend;
        _materialParticle.SetBuffer(ShaderResourceId.Camera, _camera);

        // Use default white texture if no specific texture is needed
        _particleTexture = Rendering.TextureWhite;
        _materialParticle.SetTexture(ShaderResourceId.Texture, _particleTexture);

        // Create render context
        _renderContext = Rendering.CreateRenderContext();

        // Create particle emitter
        _emitter = new ParticleEmitterBox2D(Vector2.Zero, new Vector2(50, 10));
        _emitter.MinSpeed = 30.0f;
        _emitter.MaxSpeed = 80.0f;

        // Create particle system
        _particleSystem = Rendering.CreateParticleSystem2DCPU(_materialParticle, _emitter);
        _particleSystem.EmissionRateOverTime = 100;
        _particleSystem.ParticleLifetime = 3.0f;
        _particleSystem.MaxParticles = 3000;
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

        float emissionRate = _particleSystem.EmissionRateOverTime;
        if (ImGui.SliderFloat("Emission Rate", ref emissionRate, 10, 1000))
        {
            _particleSystem.EmissionRateOverTime = emissionRate;
        }

        float lifetime = _particleSystem.ParticleLifetime;
        if (ImGui.SliderFloat("Lifetime", ref lifetime, 0.5f, 10.0f))
        {
            _particleSystem.ParticleLifetime = lifetime;
        }

        Vector2 position = _emitter.Position;
        if (ImGui.SliderFloat2("Emitter Position", ref position, -300, 300))
        {
            _emitter.Position = position;
        }

        Vector2 extents = _emitter.Extents;
        if (ImGui.SliderFloat2("Emitter Size", ref extents, 1, 100))
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

        ImGui.End();
    }

    protected override void OnStop()
    {
        _particleSystem.Dispose();
        _renderContext.Dispose();
    }
}