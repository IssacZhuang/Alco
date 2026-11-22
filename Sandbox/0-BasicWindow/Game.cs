using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Engine;
using Alco.Graphics;
using Buffer = Alco.Graphics.Buffer;

/// <summary>Sandbox that renders the classic Vulkan RGB triangle using RenderContext and Material.</summary>
public class Game : GameEngine
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector4 Position;
        public Vector4 Color;
    }

    private static readonly Vertex[] TriangleVertices =
    [
        new Vertex { Position = new Vector4(0.0f, 0.5f, 0.0f, 1.0f), Color = new Vector4(1.0f, 0.0f, 0.0f, 1.0f) },
        new Vertex { Position = new Vector4(0.5f, -0.5f, 0.0f, 1.0f), Color = new Vector4(0.0f, 1.0f, 0.0f, 1.0f) },
        new Vertex { Position = new Vector4(-0.5f, -0.5f, 0.0f, 1.0f), Color = new Vector4(0.0f, 0.0f, 1.0f, 1.0f) },
    ];

    private readonly RenderContext _renderContext;
    private readonly Buffer _vertexBuffer;
    private readonly Material _material;

    /// <summary>Initializes the triangle vertex buffer, material, and pipeline.</summary>
    /// <param name="setting">Engine settings for the game instance.</param>
    public Game(GameEngineSetting setting) : base(setting)
    {
        _renderContext = new RenderContext(RenderingSystem);
        _vertexBuffer = RenderingSystem.CreateBuffer<Vertex>(TriangleVertices);

        Shader shader = ShaderSystem.GetShader("Triangle");
        _material = shader.CreateMaterial("classic_triangle");
        _material.SetBuffer("Vertices", _vertexBuffer);
    }

    /// <summary>Renders the classic RGB triangle into the swapchain frame.</summary>
    /// <param name="delta">Elapsed time since the previous frame in seconds.</param>
    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (!MainPresenter.HasValidFrame)
        {
            return;
        }

        bool begun = false;
        try
        {
            _renderContext.BeginFrame();
            begun = true;

            ColorAttachment color = new()
            {
                RenderView = MainPresenter.CurrentFrame.RenderView,
                Load = LoadOp.Clear,
                Store = StoreOp.Store,
                Clear = new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
            };
            RenderingDesc pass = new() { Colors = [color] };
            using (RenderContext.RenderScope render = _renderContext.BeginRenderPass(pass))
            {
                render.Draw(_material, 3);
            }

            MainPresenter.EnqueueFrameContext(_renderContext);
        }
        catch
        {
            if (begun)
            {
                _renderContext.DiscardFrame();
            }
            throw;
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        GraphicsDevice.WaitIdle();
        _renderContext.Destroy();
        _vertexBuffer.Destroy();
        base.Dispose();
    }
}
