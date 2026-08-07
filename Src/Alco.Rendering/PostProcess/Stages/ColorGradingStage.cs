
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Post-process stage that applies procedural color grading to the scene. Falls back to a
/// plain copy while the parameters are at identity.
/// </summary>
public sealed class ColorGradingStage : PostProcessStage
{
    private readonly RenderingSystem _rendering;
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Material _gradingMaterial;
    private readonly GraphicsBuffer _dataBuffer;

    private ColorGradingData _data;
    private RenderTexture? _boundSource;

    /// <inheritdoc />
    public override int Order => 100;

    /// <summary>
    /// The color grading parameters. Updating this property immediately uploads to the GPU.
    /// </summary>
    public ColorGradingData Data
    {
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

    /// <summary>
    /// Creates the stage. The shader stays owned by the caller.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="gradingShader">The color grading shader.</param>
    public ColorGradingStage(RenderingSystem rendering, Shader gradingShader)
    {
        _rendering = rendering;
        _renderContext = rendering.CreateRenderContext();
        _fullScreenMesh = rendering.MeshFullScreen;

        _gradingMaterial = rendering.CreateMaterial(gradingShader);

        _data = ColorGradingData.Default;
        _dataBuffer = rendering.CreateGraphicsBuffer((uint)Unsafe.SizeOf<ColorGradingData>(), "color_grading_data");
        _dataBuffer.UpdateBuffer(_data);
        _gradingMaterial.SetBuffer(ShaderResourceId.Data, _dataBuffer);
    }

    /// <inheritdoc />
    public override void Apply(PostProcessContext context)
    {
        if (_data.IsIdentity)
        {
            context.Chain.Blit(context.Source, context.Destination);
            return;
        }

        if (!ReferenceEquals(_boundSource, context.Source))
        {
            _gradingMaterial.SetRenderTexture(ShaderResourceId.Texture, context.Source);
            _boundSource = context.Source;
        }

        _renderContext.Begin(context.Destination);
        _renderContext.Draw(_fullScreenMesh, _gradingMaterial);
        _renderContext.End();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gradingMaterial.Dispose();
            _dataBuffer.Dispose();
            _renderContext.Dispose();
        }
    }
}
