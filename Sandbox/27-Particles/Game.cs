using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;
using Alco.ImGUI;


public class Game : GameEngine
{
    private readonly Camera2DBuffer _camera;
    private readonly Material _materialParticle;
    private readonly ParticleEmitterBox2D _emitter;
    private readonly ParticleSimulatorColorLerp2D _simulator;
    private readonly ParticleSystem2DCPU _particleSystem;
    private readonly Mesh _mesh;



    private RenderContext _renderContext;
    private SubRenderContext _subRenderContext;
    private InstanceRenderer<ParticleData2D> _renderer;

    private Texture2D _particleTexture;
    private float _boxRotation = 0f;
    private readonly string[] spaceModes = new string[] { "Local", "World" };
    private readonly string[] operationModes = new string[] { "Translate", "Scale", "Rotate" };
    private int _operationIndex = 0;
    private OPERATION _imGuizmoOperation = OPERATION.TRANSLATE_X | OPERATION.TRANSLATE_Y;

    public Game(GameEngineSetting setting) : base(setting)
    {
        // Create camera
        _camera = RenderingSystem.CreateCamera2D(64, 36, 100);

        // Create material for particles
        _materialParticle = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_Particle2D);
        _materialParticle.BlendState = BlendState.Additive;
        _materialParticle.SetBuffer(ShaderResourceId.Camera, _camera);

        // Use default white texture if no specific texture is needed
        _particleTexture = AssetSystem.Load<Texture2D>("Droplet");
        _materialParticle.SetTexture(ShaderResourceId.Texture, _particleTexture);

        // Create render context
        _renderContext = RenderingSystem.CreateRenderContext();
        _subRenderContext = RenderingSystem.CreateSubRenderContext();

        _mesh = RenderingSystem.MeshCenteredSprite;

        _renderer = RenderingSystem.CreateInstanceRenderer<ParticleData2D>(_subRenderContext, _materialParticle, "_particles", 512 * 1024, "ParticleRenderer");

        // Create particle emitter
        _emitter = new ParticleEmitterBox2D(Vector2.Zero, new Vector2(0, 0));
        _emitter.MinSpeed = 8.0f;
        _emitter.MaxSpeed = 15.0f;

        _emitter.MinRotation = 270f;
        _emitter.MaxRotation = 270f;

        _emitter.MinSize = _emitter.MaxSize = 2.5f;

        _simulator = new ParticleSimulatorColorLerp2D();

        // Create particle system
        _particleSystem = new(_emitter, _simulator)
        {
            EmissionRateOverTime = 100,
            ParticleLifetime = 1.0f,
            MaxParticles = 100000
        };
        _particleSystem.Play();

    }

    protected override void OnTick(float delta)
    {
        // Simulate particles
        _particleSystem.Simulate(delta);
        _subRenderContext.Begin(MainFrameBuffer.AttachmentLayout);
        _renderer.DrawWithConstant(_mesh, _particleSystem.Transform.Matrix, _particleSystem.Particles);
        _subRenderContext.End();
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        ImGuizmo.Manipulate(_camera.Data.ViewMatrix, _camera.Data.ProjectionMatrix, _imGuizmoOperation, MODE.LOCAL, ref _particleSystem.Transform);

        // Draw particles
        if (_subRenderContext.HasBuffer)
        {
            _renderContext.Begin(MainFrameBuffer);
            _renderContext.ExecuteSubContext(_subRenderContext);
            _renderContext.End();
        }


        // Show particle controls
        ImGui.Begin("Particle Control");

        FixedString8 strFramerate = new FixedString8();//zero allocation string
        strFramerate.Append(FrameRate);

        ImGui.Text(strFramerate);
        ImGui.Text($"Particle Count: {_particleSystem.Particles.Length}");

        ImGui.EditTransform2D(ref _particleSystem.Transform);

        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Space Mode");
        ImGui.Text($"Rotation: {_particleSystem.Transform.Rotation.ToRadian() / math.DegToRad}");

        int currentSpaceMode = (int)_particleSystem.SpaceMode;

        if (ImGui.Combo("Particle Space Mode", ref currentSpaceMode, spaceModes, spaceModes.Length))
        {
            _particleSystem.SpaceMode = (SpaceMode)currentSpaceMode;
        }


        if (ImGui.Combo("Particle Operation Mode", ref _operationIndex, operationModes, operationModes.Length))
        {
            switch(_operationIndex)
            {
                case 0:
                    _imGuizmoOperation = OPERATION.TRANSLATE_X | OPERATION.TRANSLATE_Y;
                    break;
                case 1:
                    _imGuizmoOperation = OPERATION.SCALE_X | OPERATION.SCALE_Y;
                    break;
                case 2:
                    _imGuizmoOperation = OPERATION.ROTATE;
                    break;
            }   
        }

        // Space Mode Controls
        ImGui.Separator();

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

        Vector2 position = _emitter.Shape.Center;
        if (ImGui.SliderFloat2("Box Position", ref position, -300, 300))
        {
            _emitter.Shape.Center = position;
        }

        Vector2 extents = _emitter.Shape.Extends;
        if (ImGui.SliderFloat2("Box Extents", ref extents, 0, 10))
        {
            _emitter.Shape.Extends = extents;
        }

        if (ImGui.SliderFloat("Box Rotation", ref _boxRotation, 0, 360))
        {
            _emitter.Shape.Rotation = new Rotation2D(_boxRotation);
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

        float coneAngle = _emitter.ConeAngle;
        if (ImGui.SliderFloat("Cone Angle", ref coneAngle, 0, 360))
        {
            _emitter.ConeAngle = coneAngle;
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