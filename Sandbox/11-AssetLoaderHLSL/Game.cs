using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Vocore.Graphics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore.IO;

public class Game : GameEngine
{
    #region Shader Data

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 Position;
        public Vector3 Color;
        public Vector2 TexCoord;
    }

    private static readonly Vertex[] Vertices =
    {
        new Vertex {Position = new Vector3(-0.5f, 0.5f, 0.5f), Color = new Vector3(1.0f, 0.0f, 0.0f), TexCoord = new Vector2(0.0f, 0.0f)},
        new Vertex {Position = new Vector3(0.5f, 0.5f, 0.5f), Color = new Vector3(0.0f, 1.0f, 0.0f), TexCoord = new Vector2(1.0f, 0.0f)},
        new Vertex {Position = new Vector3(0.5f, -0.5f, 0.5f), Color = new Vector3(0.0f, 0.0f, 1.0f), TexCoord = new Vector2(1.0f, 1.0f)},
        new Vertex {Position = new Vector3(-0.5f, -0.5f, 0.5f), Color = new Vector3(1.0f, 1.0f, 1.0f), TexCoord = new Vector2(0.0f, 1.0f)}
    };

    private static readonly ushort[] Indices = { 0, 1, 2, 0, 2, 3 };


    #endregion

    private GPUCommandBuffer _commandBuffer;
    private GPUBuffer _vertexBuffer;
    private GPUBuffer _indexBuffer;
    private GPUBuffer _colorBuffer;
    private Shader _shader;//high level encapsulation of GPU pipeline
    private GPUResourceGroup _resourceGroupBuffer;
    private Texture2D _textureEmpty;
    private Texture2D _selected;


    private float _timer = 0.0f;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
        _vertexBuffer = CreateVertexBuffer();
        _indexBuffer = CreateIndexBuffer();

        DirectoryFileSource fileSource = new DirectoryFileSource("Assets");
        Assets.AddFileSource(fileSource);

        _colorBuffer = GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Color Buffer",
            Size = (uint)Marshal.SizeOf<Vector4>(),
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst
        });

        if (Assets.TryLoad("Shader.hlsl", out Shader? shader))
        {
            _shader = shader;
        }
        else
        {
            throw new Exception("Shader not found");
        }
        _resourceGroupBuffer = CreateResourceGroup(GraphicsDevice.BindGroupUniformBuffer, _colorBuffer);

        _textureEmpty = Rendering.CreateTexture2D(16, 16, 0xffffffff);
        _selected = _textureEmpty;
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (Input.IsKeyDown(KeyCode.Space))
        {
            Assets.LoadAsync<Texture2D>("test.png", (texture) =>
            {
                _selected = texture;
            });
        }

        _timer += delta;

        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(MainFrameBuffer);
        _commandBuffer.SetGraphicsPipeline(_shader.DefaultPipeline);
        _commandBuffer.SetVertexBuffer(0, _vertexBuffer);
        _commandBuffer.SetIndexBuffer(_indexBuffer, IndexFormat.Uint16);
        _commandBuffer.SetGraphicsResources(0, _resourceGroupBuffer);
        _commandBuffer.SetGraphicsResources(1, _selected.EntrySample);
        _commandBuffer.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);
        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }


    private GPUBuffer CreateIndexBuffer()
    {
        return GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Quad Index Buffer",
            Size = (uint)Marshal.SizeOf<ushort>() * (uint)Indices.Length,
            Usage = BufferUsage.Index | BufferUsage.CopyDst
        }, Indices);
    }

    private GPUBuffer CreateVertexBuffer()
    {
        return GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Quad Vertex Buffer",
            Size = (uint)Marshal.SizeOf<Vertex>() * (uint)Vertices.Length,
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst
        }, Vertices);
    }

    private GPUResourceGroup CreateResourceGroup(GPUBindGroup bindGroup, GPUBuffer buffer)
    {
        return GraphicsDevice.CreateResourceGroup(new ResourceGroupDescriptor
        {
            Layout = bindGroup,
            Resources = new ResourceBindingEntry[]
            {
                new ResourceBindingEntry(0, buffer),
            }
        });
    }

}