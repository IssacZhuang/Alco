using System.Runtime.CompilerServices;
using Alco;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Engine system that applies procedural color grading to the scene.
/// Runs in <see cref="OnPostSceneUpdate"/> between scene and UI rendering.
/// Uses a ping-pong pattern: main RT → temp RT (with grading) → main RT (copy).
/// </summary>
public class ColorGradingSystem : BaseEngineSystem
{
    private readonly ViewRenderTarget _renderTarget;
    private readonly RenderingSystem _rendering;
    private readonly RenderContext _renderContext;
    private readonly Mesh _mesh;
    private readonly Material _gradingMaterial;
    private readonly Material _blitMaterial;
    private readonly GraphicsBuffer _dataBuffer;
    private readonly Shader _blitShader;
    private readonly GPUAttachmentLayout _attachmentLayout;

    private RenderTexture? _tempTexture;
    private uint _tempWidth;
    private uint _tempHeight;
    private bool _isEnabled = true;
    private ColorGradingData _data;

    /// <summary>
    /// The execution order. Runs after scene rendering (OnUpdate) but before UI rendering.
    /// </summary>
    public override int Order => 500;

    /// <summary>
    /// Gets or sets whether the color grading effect is enabled.
    /// </summary>
    public bool IsEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isEnabled;
        set => _isEnabled = value;
    }

    /// <summary>
    /// The color grading parameters. Updating this property immediately uploads to the GPU.
    /// </summary>
    public ColorGradingData Data
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _data;
        set
        {
            _data = value;
            _dataBuffer.UpdateBuffer(value);
        }
    }

    /// <summary>
    /// Direct access to the underlying GPU buffer for batch updates without per-field uploads.
    /// Call <see cref="GraphicsBuffer.UpdateBuffer{T}(T)"/> after modifying <see cref="Data"/> directly.
    /// </summary>
    public GraphicsBuffer DataBuffer => _dataBuffer;

    internal ColorGradingSystem(GameEngine engine, ViewRenderTarget renderTarget, Shader gradingShader)
    {
        _renderTarget = renderTarget;
        _rendering = engine.RenderingSystem;

        _renderContext = _rendering.CreateRenderContext();
        _mesh = _rendering.MeshFullScreen;
        _attachmentLayout = renderTarget.RenderTexture.AttachmentLayout;

        _blitShader = engine.AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_Blit);
        _blitMaterial = _rendering.CreateMaterial(_blitShader);

        _gradingMaterial = _rendering.CreateMaterial(gradingShader);
        _dataBuffer = _rendering.CreateGraphicsBuffer((uint)System.Runtime.CompilerServices.Unsafe.SizeOf<ColorGradingData>(), "color_grading_data");
        _dataBuffer.UpdateBuffer(_data);
        _gradingMaterial.SetBuffer(ShaderResourceId.Data, _dataBuffer);

        _data = ColorGradingData.Default;

        renderTarget.OnResize += OnRenderTargetResize;
    }

    /// <summary>
    /// Applies color grading to the scene. Called between scene and UI rendering.
    /// Skips GPU work when disabled or parameters are at identity.
    /// </summary>
    public override void OnPostSceneUpdate(float delta)
    {
        if (!_isEnabled || _data.IsIdentity)
        {
            return;
        }

        RenderTexture mainRT = _renderTarget.RenderTexture;
        EnsureTempTexture(mainRT.Width, mainRT.Height);

        // Pass 1: Color grade from main RT to temp RT
        _gradingMaterial.SetRenderTexture(ShaderResourceId.Texture, mainRT);
        _renderContext.Begin(_tempTexture!.FrameBuffer);
        _renderContext.Draw(_mesh, _gradingMaterial);
        _renderContext.End();

        // Pass 2: Copy temp RT back to main RT
        _blitMaterial.SetRenderTexture(ShaderResourceId.Texture, _tempTexture);
        _renderContext.Begin(mainRT.FrameBuffer);
        _renderContext.Draw(_mesh, _blitMaterial);
        _renderContext.End();
    }

    private void EnsureTempTexture(uint width, uint height)
    {
        if (_tempTexture != null && _tempWidth == width && _tempHeight == height)
        {
            return;
        }

        _tempTexture?.Dispose();
        _tempTexture = _rendering.CreateRenderTexture(_attachmentLayout, width, height, "color_grading_temp");
        _tempWidth = width;
        _tempHeight = height;
    }

    private void OnRenderTargetResize(uint2 size)
    {
        // Temp texture will be recreated on next frame via EnsureTempTexture
        _tempTexture?.Dispose();
        _tempTexture = null;
        _tempWidth = 0;
        _tempHeight = 0;
    }

    public override void Dispose()
    {
        _renderTarget.OnResize -= OnRenderTargetResize;
        _gradingMaterial.Dispose();
        _blitMaterial.Dispose();
        _dataBuffer.Dispose();
        _tempTexture?.Dispose();
        GC.SuppressFinalize(this);
    }
}
