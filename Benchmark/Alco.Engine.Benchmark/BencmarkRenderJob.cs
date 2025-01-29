using System.Numerics;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine.Benchmark;

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
    private GPUCommandBuffer _commandBuffer;
    private Constant _contant;

    private const int _drawCount = 128000;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new GameEngine(GameEngineSetting.CreateNoGPU());
        RenderingSystem renderingSystem = _engine.Rendering;
        

        _shader = _engine.BuiltInAssets.Shader_Sprite;
        _material = renderingSystem.CreateGraphicsMaterial(_shader);
        _mesh = renderingSystem.MeshCenteredSprite;
        _renderTexture = renderingSystem.CreateRenderTexture(renderingSystem.PrefferedHDRPass, 1024, 1024);

        _renderJobEncode = new RenderJobDrawMesh(_renderTexture.FrameBuffer);
        _renderJobExecute = new RenderJobDrawMesh(_renderTexture.FrameBuffer);

        _contant = new Constant
        {
            Model = Transform2D.Identity.Matrix,
            Color = new Vector4(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };


        for (int i = 0; i < _drawCount; i++)
        {
            _renderJobExecute.SetMesh(_mesh);
            _renderJobExecute.SetMaterial(_material);
            _renderJobExecute.DrawWithConstant(_contant);
        }
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
            _renderJobEncode.SetMaterial(_material);
            _renderJobEncode.SetMesh(_mesh);
            _renderJobEncode.DrawWithConstant(_contant);
        }
        _renderJobEncode.Reset();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _commandBuffer = _engine.GraphicsDevice.CreateCommandBuffer();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _commandBuffer.Dispose();
    }

    [Benchmark(Description = "RenderJobDrawMesh Execute")]
    public void RenderJobDrawMeshExecute()
    {
        _commandBuffer.Begin();
        _renderJobExecute.Execute(_commandBuffer);
        _commandBuffer.End();
        _engine.GraphicsDevice.Submit(_commandBuffer);
    }
}