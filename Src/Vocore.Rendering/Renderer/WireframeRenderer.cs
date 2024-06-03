using System.Numerics;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class WireframeRenderer : AutoDisposable
{
    public struct Vertex
    {
        public Vector3 Position;
        public Vector4 Color;
    }

    private readonly Mesh _mesh;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;

    private readonly Shader _shader;
    private GPURenderPass? _renderPass;
    private GPUPipeline? _pipeline;
    private readonly uint _shaderId_camera;


    private NativeArrayList<Vertex> _vertices;
    private NativeArrayList<uint> _indices;

    public GraphicsBuffer Camera { get; set; }



    internal WireframeRenderer(RenderingSystem renderingSystem, GraphicsBuffer camera, Shader shader)
    {
        Camera = camera;

        _mesh = renderingSystem.CreateMesh("wireframe");
        _device = renderingSystem.GraphicsDevice;
        _command = _device.CreateCommandBuffer(new CommandBufferDescriptor("wireframe_renderer_command"));
        _vertices = new NativeArrayList<Vertex>();
        _indices = new NativeArrayList<uint>();

        _shader = shader;
        _shaderId_camera = shader.GetResourceId("camera");
    }

    public void AddLine(Vector3 start, Vector3 end, Vector4 color)
    {
        _vertices.Add(new Vertex { Position = start, Color = color });
        _vertices.Add(new Vertex { Position = end, Color = color });

        uint index = (uint)(_vertices.Count - 2);
        _indices.Add(index);
        _indices.Add(index + 1);
    }

    public void Clear()
    {
        _vertices.Clear();
        _indices.Clear();
    }



    public void Begin(GPUFrameBuffer target)
    {
        if (_vertices.Length == 0)
        {
            return;
        }

        if (_renderPass != target.RenderPass)
        {
            _renderPass = target.RenderPass;
            _pipeline = _shader.GetPipelineVariant(_renderPass);
        }

        _command.Begin();
        _command.SetFrameBuffer(target);
        _command.SetVertexBuffer(0, _mesh.VertexBuffer);
        _command.SetIndexBuffer(_mesh.IndexBuffer, _mesh.IndexFormat);
        _command.SetGraphicsPipeline(_pipeline!);
        _command.SetGraphicsResources(_shaderId_camera, Camera.EntryReadonly);

    }

    public void End()
    {
        if (_command.IsRecording)
        {
            _command.DrawIndexed(_mesh.IndexCount, 1, 0, 0, 0);
            _command.End();
            _device.Submit(_command);
        }
    }


    protected override void Dispose(bool disposing)
    {
        //dispose native resources
        _vertices.Dispose();
        _indices.Dispose();
    }
}