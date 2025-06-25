using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;

using Random = Alco.Random;
using Alco.Graphics;
using Alco.GUI;
using Alco.ImGUI;

public class Game : GameEngine
{
    //scence
    private readonly Camera2DBuffer _camera;

    private readonly Texture2D _quad;
    private readonly Shader _spriteShader;
    private readonly RenderContext _renderContext;
    private readonly SpriteRenderer _renderer;
    private float _intensity = 3;
    private bool _enabled = true;


    public Game(GameEngineSetting setting) : base(setting)
    {

        //scence
        _spriteShader = BuiltInAssets.Shader_Sprite;
       
        _quad = RenderingSystem.CreateTexture2D(4,4, 0xffffff);

        _camera = RenderingSystem.CreateCamera2D(640, 360, 100);

        Material material = RenderingSystem.CreateMaterial(_spriteShader);
        material.SetBuffer(ShaderResourceId.Camera, _camera);
        _renderContext = RenderingSystem.CreateRenderContext("renderer");
        _renderer = RenderingSystem.CreateSpriteRenderer(_renderContext, material);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (Input.IsKeyDown(KeyCode.Up))
        {
            _intensity += 0.1f;
            Log.Info(_intensity);
        }

        if (Input.IsKeyDown(KeyCode.Down))
        {
            _intensity -= 0.1f;
            Log.Info(_intensity);
        }

        DebugStats.Text(FrameRate);

        Vector2 normalizedMousePosition = Input.MousePosition / new Vector2(1280, 720);
        Vector2 spritePosition = normalizedMousePosition * new Vector2(640, 360) - new Vector2(320, 180);
        spritePosition.Y = -spritePosition.Y;

        _renderContext.Begin(MainFrameBuffer);
        //_spriteRenderer.Draw(_star, new Vector2(0, 0), Rotation2D.Identity, Vector2.One * 20, new Vector4(1, 1, 1, 1));

        if(_enabled){
            _renderer.Draw(_quad, Vector2.Zero, Rotation2D.Identity, Vector2.One * 24, new ColorFloat(_intensity*2, _intensity, _intensity, 1));
        }
       

        _renderContext.End();

        // ImGUI Controls
        ImGui.Begin("Bloom HDR Controls");

        // Display intensity value
        FixedString32 intensityText = new FixedString32();
        intensityText.Append("Intensity: ");
        intensityText.Append(_intensity);
        ImGui.Text(intensityText);

        if (ImGui.Button("-0.1"))
        {
            _intensity -= 0.1f;
        }
        ImGui.SameLine();
        ImGui.SliderFloat("Intensity", ref _intensity, 0, 5);
        ImGui.SameLine();
        if (ImGui.Button("+0.1"))
        {
            _intensity += 0.1f;
        }
        ImGui.Checkbox("Enabled", ref _enabled);

        ImGui.End();
    }

    protected override void OnStop()
    {
       
    }
}