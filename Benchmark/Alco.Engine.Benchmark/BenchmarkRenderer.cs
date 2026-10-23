
using System.Numerics;
using Alco.Rendering;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkFramework;

namespace Alco.Engine.Benchmark;

/// <summary>
/// Benchmarks the old and new sprite renderer implementations.
/// </summary>
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


    /// <summary>
    /// Creates the engine, texture, and render target used by the benchmarks.
    /// </summary>
    public BenchmarkRenderer()
    {
        _engine = new GameEngine(GameEngineSetting.CreateGPUWithoutView());
        RenderingSystem renderingSystem = _engine.RenderingSystem;
        _texture = renderingSystem.TextureWhite;
        _target = renderingSystem.CreateRenderTexture(renderingSystem.PreferredHDRPass, 1920, 1080);
    }

    /// <summary>
    /// Initializes the camera, material, and sprite renderers.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {

        RenderingSystem renderingSystem = _engine.RenderingSystem;
        GraphicsBuffer camera = renderingSystem.CreateCamera2D(Vector2.One, 100, "camera");
        Shader shader = _engine.BuiltInAssets.Shader_Sprite;
        GraphicsMaterial material = renderingSystem.CreateGraphicsMaterial(shader);
        material.SetBuffer(ShaderResourceId.Camera, camera);
        _context = renderingSystem.CreateRenderContext();
        _renderer1 = new OldSpriteRenderer(renderingSystem, renderingSystem.MeshCenteredSprite, camera, shader);
        _renderer2 = renderingSystem.CreateSpriteRenderer(_context, material);


    }

    /// <summary>
    /// Benchmarks the legacy sprite renderer.
    /// </summary>
    [Benchmark]
    public void OldSpriteRenderer()
    {
        _renderer1.Begin(_target.FrameBuffer);
        for (int i = 0; i < count; i++)
        {
            _renderer1.Draw(_texture, Vector3.Zero, Quaternion.Identity, Vector3.One, Vector4.One);
        }
        _renderer1.End();
    }

    /// <summary>
    /// Benchmarks the current sprite renderer.
    /// </summary>
    [Benchmark]
    public void NewSpriteRenderer()
    {
        using (RenderFrameScope frame = _context.BeginFrame())
        using (RenderPassScope pass = _context.BeginPass(_target.FrameBuffer))
        {
            for (int i = 0; i < count; i++)
            {
                _renderer2.Draw(_texture, Vector3.Zero, Quaternion.Identity, Vector3.One, Vector4.One);
            }
        }
    }
}
