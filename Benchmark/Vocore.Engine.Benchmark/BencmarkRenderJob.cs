using System.Numerics;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Vocore.Rendering;

namespace Vocore.Engine.Benchmark;

public class BenchmarkRenderJob
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public Vector4 Color;
        public Rect UvRect;
    }
    private RenderJobDrawMesh _renderJobEncode;
    private RenderJobDrawMesh _renderJobExecute;
    private GameEngine _engine;
    private Shader _shader;
    private GraphicsMaterial _material;
    private Mesh _mesh;
    private RenderTexture _renderTexture;
    private Constant _contant;

    private const int _drawCount = 12800;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new GameEngine(GameEngineSetting.CreateGPUWithoutWindow());
        RenderingSystem renderingSystem = _engine.Rendering;
        _renderJobEncode = new RenderJobDrawMesh(_engine.MainFrameBuffer);
        _renderJobExecute = new RenderJobDrawMesh(_engine.MainFrameBuffer);

        _shader = _engine.BuiltInAssets.Shader_Sprite;
        _material = renderingSystem.CreateGraphicsMaterial(_shader);
        _mesh = renderingSystem.MeshSprite;
        _renderTexture = renderingSystem.CreateRenderTexture(renderingSystem.PrefferedHDRPass, 1024, 1024);

        _contant = new Constant
        {
            Model = Transform2D.Identity.Matrix,
            Color = new Vector4(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _shader.Dispose();
        _material.Dispose();
        _engine.Dispose();
    }

    [Benchmark(Description = "RenderJobDrawMesh Encode")]
    public void RenderJobDrawMeshEncode()
    {
        
        for (int i = 0; i < _drawCount; i++)
        {
            _renderJobEncode.SetMesh(_mesh);
            _renderJobEncode.SetMaterial(_material);
            _renderJobEncode.DrawWithConstant(_contant);
        }
        _renderJobEncode.Reset();
    }



}