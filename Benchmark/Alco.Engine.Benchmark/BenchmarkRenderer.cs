
using System.Numerics;
using Alco.Rendering;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkFramework;

namespace Alco.Engine.Benchmark;

[CustomConfigParam(8, 50, 128)]
public class BenchmarkRenderer
{
    const int count = 10000;
    private OldSpriteRenderer _renderer1;
    private RenderContext _context;
    private SpriteRenderer _renderer2;
    private GameEngine _engine;
    private Texture2D _texture;
    private RenderTexture _target;


    public BenchmarkRenderer()
    {
        _engine = new GameEngine(GameEngineSetting.CreateGPUWithoutView());
        RenderingSystem renderingSystem = _engine.Rendering;
        _texture = renderingSystem.TextureWhite;
        _target = renderingSystem.CreateRenderTexture(renderingSystem.PrefferedHDRPass, 1920, 1080);
    }

    [GlobalSetup]
    public void Setup()
    {

        RenderingSystem renderingSystem = _engine.Rendering;
        GraphicsBuffer camera = renderingSystem.CreateCamera2D(Vector2.One, 100, "camera");
        Shader shader = _engine.BuiltInAssets.Shader_Sprite;
        Material material = renderingSystem.CreateGraphicsMaterial(shader);
        material.SetBuffer(ShaderResourceId.Camera, camera);
        _context = renderingSystem.CreateRenderContext();
        _renderer1 = new OldSpriteRenderer(renderingSystem, renderingSystem.MeshCenteredSprite, camera, shader);
        _renderer2 = renderingSystem.CreateSpriteRenderer(_context, material);


    }

    [Benchmark]
    public void OldSpriteRenderer()
    {
        _renderer1.Begin(_engine.MainFrameBuffer);
        for (int i = 0; i < count; i++)
        {
            _renderer1.Draw(_texture, Vector3.Zero, Quaternion.Identity, Vector3.One, Vector4.One);
        }
        _renderer1.End();
    }

    [Benchmark]
    public void NewSpriteRenderer()
    {
        _context.Begin(_engine.MainFrameBuffer);
        for (int i = 0; i < count; i++)
        {
            _renderer2.Draw(_texture, Vector3.Zero, Quaternion.Identity, Vector3.One, Vector4.One);
        }
        _context.End();
    }
}
