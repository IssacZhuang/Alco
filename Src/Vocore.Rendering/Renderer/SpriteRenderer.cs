using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The renderer to draw sprites in 2D or 3D space.
/// <br/> Not thread safe but each thread can have its own renderer instance for multi-thread rendering.
/// </summary>
public class SpriteRenderer : AutoDisposable, IRenderer
{
    public const string ShaderId_camera = "_camera";
    public const string ShaderId_texture = "_texture";

    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public Vector4 Color;
        public Rect UvRect;
    }

    private class DrawTask : RenderTask
    {
        private struct DrawData
        {
            public Texture2D Texture;
            public Constant Constant;
        }





        private readonly GraphicsBuffer _camera;
        private readonly Mesh _mesh;
        private readonly Shader _shader;

        private GraphicsPipelineContext _pipelineInfo;
        private uint _shaderId_texture;
        private uint _shaderId_camera;

        private readonly DrawData[] drawDatas;
        private int _capacity;
        private int _drawCount = 0;

        public int DrawCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _drawCount;
        }

        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _capacity;
        }

        public bool IsFull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _drawCount >= _capacity;
        }

        public DrawTask(
            RenderingSystem renderingSystem,
            GraphicsBuffer camera,
            Shader shader,
            Mesh mesh,
            int capacity = 10000
            ) : base(renderingSystem, 1)
        {
            _camera = camera;
            _shader = shader;
            _mesh = mesh;

            _pipelineInfo = shader.GetGraphicsPipeline(
                renderingSystem.PrefferedSDRPass,
                DepthStencilState.Read,
                BlendState.AlphaBlend
            );

            _shaderId_camera = _pipelineInfo.GetResourceId(ShaderId_camera);
            _shaderId_texture = _pipelineInfo.GetResourceId(ShaderId_texture);

            _capacity = capacity;
            drawDatas = new DrawData[_capacity];
        }

        protected override void ExecuteCore(GPUCommandBuffer commandBuffer, GPUFrameBuffer renderTarget)
        {
            if (_shader.TryUpdatePipelineContext(ref _pipelineInfo, renderTarget.RenderPass))
            {
                _shaderId_camera = _pipelineInfo.GetResourceId(ShaderId_camera);
                _shaderId_texture = _pipelineInfo.GetResourceId(ShaderId_texture);
            }

            commandBuffer.SetGraphicsPipeline(_pipelineInfo);
            commandBuffer.SetGraphicsResources(_shaderId_camera, _camera.EntryReadonly);
            commandBuffer.SetVertexBuffer(0, _mesh.VertexBuffer);
            commandBuffer.SetIndexBuffer(_mesh.IndexBuffer, _mesh.IndexFormat);


            for (int i = 0; i < _drawCount; i++)
            {
                DrawData drawData = drawDatas[i];
                commandBuffer.SetGraphicsResources(_shaderId_texture, drawData.Texture.EntrySample);
                commandBuffer.PushConstants(ShaderStage.Vertex | ShaderStage.Fragment, drawData.Constant);
                commandBuffer.DrawIndexed(_mesh.IndexCount, 1, 0, 0, 0);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddDrawData(Texture2D texture, Constant constant)
        {
            drawDatas[_drawCount++] = new DrawData { Texture = texture, Constant = constant };
        }

        public void Clear()
        {
            _drawCount = 0;
        }
    }

    private static readonly Rect DefaultUvRect = new Rect(0, 0, 1, 1);

    private readonly RenderingSystem _renderingSystem;
    private readonly Shader _shader;
    private readonly Mesh _mesh;

    private GPUFrameBuffer? _renderTarget;

    private GraphicsPipelineContext _pipelineInfo;

    private uint _shaderId_camera;
    private uint _shaderId_texture;

    private readonly DynamicCircularBuffer<DrawTask> _drawTasks;
    private DrawTask? _currentTask;

    public GraphicsBuffer Camera { get; set; }


    internal SpriteRenderer(RenderingSystem renderingSystem, Mesh mesh, GraphicsBuffer camera, Shader shader)
    {
        _renderingSystem = renderingSystem;
        _shader = shader;
        _mesh = mesh;

        _pipelineInfo = shader.GetGraphicsPipeline(
            renderingSystem.PrefferedSDRPass,
            DepthStencilState.Read,
            BlendState.AlphaBlend
        );

        _shaderId_camera = _pipelineInfo.GetResourceId(ShaderId_camera);
        _shaderId_texture = _pipelineInfo.GetResourceId(ShaderId_texture);

        Camera = camera;

        _drawTasks = new DynamicCircularBuffer<DrawTask>(() => new DrawTask(_renderingSystem, Camera, _shader, _mesh));
    }

    public void Begin(GPUFrameBuffer target)
    {
        // if (_shader.TryUpdatePipelineContext(ref _pipelineInfo, target.RenderPass))
        // {
        //     _shaderId_camera = _pipelineInfo.GetResourceId(ShaderId_camera);
        //     _shaderId_texture = _pipelineInfo.GetResourceId(ShaderId_texture);
        // }

        // _command.Begin();
        // _command.SetFrameBuffer(target);
        // _command.SetGraphicsPipeline(_pipelineInfo);
        // _command.SetGraphicsResources(_shaderId_camera, Camera.EntryReadonly);
        // _command.SetVertexBuffer(0, _mesh.VertexBuffer);
        // _command.SetIndexBuffer(_mesh.IndexBuffer, _mesh.IndexFormat);
        _currentTask = _drawTasks.Next();
        this._renderTarget = target ?? throw new ArgumentNullException(nameof(target));
    }

    public void End()
    {
        //run last task
        _currentTask!.Run(_renderTarget!);
        for (int i = 0; i < _drawTasks.UsedCount; i++)
        {
            _drawTasks[i].Submit();
            _drawTasks[i].Clear();
        }
        _drawTasks.ResetToHead();
    }

    #region  Draw 3D

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Vector3 position, Quaternion rotation, Vector3 scale, ColorFloat color)
    {
        Transform3D transform = new Transform3D(position, rotation, scale);
        DrawCore(texture, DefaultUvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Vector3 position, Quaternion rotation, Vector3 scale, Rect uvRect, ColorFloat color)
    {
        Transform3D transform = new Transform3D(position, rotation, scale);
        DrawCore(texture, uvRect, transform.Matrix, color);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Transform3D transform, ColorFloat color)
    {
        DrawCore(texture, DefaultUvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Transform3D transform, Rect uvRect, ColorFloat color)
    {
        DrawCore(texture, uvRect, transform.Matrix, color);
    }

    #endregion

    #region Draw 2D

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Vector2 position, Rotation2D rotation, Vector2 scale, ColorFloat color)
    {
        Transform2D transform = new Transform2D(position, rotation, scale);
        DrawCore(texture, DefaultUvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Vector2 position, Rotation2D rotation, Vector2 scale, Rect uvRect, ColorFloat color)
    {
        Transform2D transform = new Transform2D(position, rotation, scale);
        DrawCore(texture, uvRect, transform.Matrix, color);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Transform2D transform, ColorFloat color)
    {
        DrawCore(texture, DefaultUvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Transform2D transform, Rect uvRect, ColorFloat color)
    {
        DrawCore(texture, uvRect, transform.Matrix, color);
    }

    #endregion

    
    #region Draw by matrix

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Matrix4x4 matrix, ColorFloat color)
    {
        DrawCore(texture, DefaultUvRect, matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Matrix4x4 matrix, Rect uvRect, ColorFloat color)
    {
        DrawCore(texture, uvRect, matrix, color);
    }

    #endregion

    private void DrawCore(Texture2D texture, Rect uvRect, Matrix4x4 matrix, ColorFloat color)
    {
        Constant constant = new Constant
        {
            Model = matrix,
            Color = color,
            UvRect = uvRect
        };

        if (_currentTask!.IsFull)
        {
            _currentTask.Run(_renderTarget!);
            _currentTask = _drawTasks.Next();
        }

        _currentTask.AddDrawData(texture, constant);
    }

    protected override void Dispose(bool disposing)
    {

    }
}