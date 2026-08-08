
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Content processor node that applies procedural color grading to the input. Falls back
/// to a plain copy while the parameters are at identity.
/// </summary>
public sealed class RenderNode_ColorGrading : AutoDisposable, IContentProcessorNode
{
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Material _gradingMaterial;
    private readonly Material _blitMaterial;
    private readonly GraphicsBuffer _dataBuffer;

    private ColorGradingData _data;

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

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
    /// Creates the node. The shaders stay owned by the caller.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="gradingShader">The color grading shader.</param>
    /// <param name="blitShader">The shader used for the plain copy at identity parameters.</param>
    public RenderNode_ColorGrading(RenderingSystem rendering, Shader gradingShader, Shader blitShader)
    {
        _renderContext = rendering.CreateRenderContext();
        _fullScreenMesh = rendering.MeshFullScreen;

        _gradingMaterial = rendering.CreateMaterial(gradingShader);
        _blitMaterial = rendering.CreateMaterial(blitShader);

        _data = ColorGradingData.Default;
        _dataBuffer = rendering.CreateGraphicsBuffer((uint)Unsafe.SizeOf<ColorGradingData>(), "color_grading_data");
        _dataBuffer.UpdateBuffer(_data);
        _gradingMaterial.SetBuffer(ShaderResourceId.Data, _dataBuffer);
    }

    /// <inheritdoc />
    public void OnRenderForward(RenderTexture input, RenderTexture target)
    {
        Material material = _data.IsIdentity ? _blitMaterial : _gradingMaterial;
        material.SetRenderTexture(ShaderResourceId.Texture, input);
        _renderContext.Begin(target.FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, material);
        _renderContext.End();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gradingMaterial.Dispose();
            _blitMaterial.Dispose();
            _dataBuffer.Dispose();
            _renderContext.Dispose();
        }
    }
}
