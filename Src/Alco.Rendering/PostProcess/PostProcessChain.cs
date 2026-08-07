
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// An ordered chain of <see cref="PostProcessStage"/> owned by a <see cref="RenderPipeline"/>.
/// Executes the enabled stages in ascending order against the pipeline's scene texture:
/// intermediate stages ping-pong through chain-owned temporary textures and the last enabled
/// stage writes directly into the final destination, so the chain never performs a redundant
/// blit. With no enabled stage the chain degrades to a single plain blit.
/// </summary>
public sealed class PostProcessChain : AutoDisposable
{
    private readonly RenderingSystem _rendering;
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Material _blitMaterial;
    private readonly List<PostProcessStage> _stages = new();

    private GPUAttachmentLayout? _tempLayout;
    private PixelFormat _tempFormat;
    private RenderTexture? _tempA;
    private RenderTexture? _tempB;

    /// <summary>
    /// The registered stages, sorted by <see cref="PostProcessStage.Order"/>.
    /// </summary>
    public IReadOnlyList<PostProcessStage> Stages => _stages;

    /// <summary>
    /// Creates an empty chain.
    /// </summary>
    /// <param name="rendering">The rendering system for creating GPU resources.</param>
    /// <param name="blitShader">The shader used for plain copies between textures.</param>
    public PostProcessChain(RenderingSystem rendering, Shader blitShader)
    {
        _rendering = rendering;
        _renderContext = rendering.CreateRenderContext();
        _fullScreenMesh = rendering.MeshFullScreen;
        _blitMaterial = rendering.CreateMaterial(blitShader);
    }

    /// <summary>
    /// Registers a stage. The chain takes ownership and disposes the stage with itself.
    /// </summary>
    public void Add(PostProcessStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        _stages.Add(stage);
        _stages.Sort(static (x, y) => x.Order.CompareTo(y.Order));
    }

    /// <summary>
    /// Removes a stage previously added via <see cref="Add"/>. The stage is not disposed.
    /// </summary>
    public bool Remove(PostProcessStage stage)
    {
        return _stages.Remove(stage);
    }

    /// <summary>
    /// Gets the first stage of the given type, or null when the chain has none.
    /// </summary>
    public T? Get<T>() where T : PostProcessStage
    {
        for (int i = 0; i < _stages.Count; i++)
        {
            if (_stages[i] is T stage)
            {
                return stage;
            }
        }
        return null;
    }

    /// <summary>
    /// Runs the enabled stages against <paramref name="source"/>, writing the final result
    /// into <paramref name="destination"/>.
    /// </summary>
    /// <param name="source">The pipeline's scene texture.</param>
    /// <param name="destination">The final output frame buffer (e.g. the swapchain).</param>
    public void Execute(RenderTexture source, GPUFrameBuffer destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        PostProcessStage? lastEnabled = null;
        int enabledCount = 0;
        for (int i = 0; i < _stages.Count; i++)
        {
            if (_stages[i].IsEnabled)
            {
                lastEnabled = _stages[i];
                enabledCount++;
            }
        }

        if (lastEnabled == null)
        {
            Blit(source, destination);
            return;
        }

        if (enabledCount > 1)
        {
            EnsureTemporaries(source);
        }

        RenderTexture current = source;
        int toggle = 0;
        for (int i = 0; i < _stages.Count; i++)
        {
            PostProcessStage stage = _stages[i];
            if (!stage.IsEnabled)
            {
                continue;
            }

            bool isLast = ReferenceEquals(stage, lastEnabled);
            GPUFrameBuffer stageDestination = isLast ? destination : GetTemporary(toggle).FrameBuffer;
            stage.Apply(new PostProcessContext(this, current, stageDestination, source));

            if (!isLast)
            {
                current = GetTemporary(toggle);
                toggle ^= 1;
            }
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="destination"/> unchanged.
    /// </summary>
    public void Blit(RenderTexture source, GPUFrameBuffer destination)
    {
        _blitMaterial.SetRenderTexture(ShaderResourceId.Texture, source);
        _renderContext.Begin(destination);
        _renderContext.Draw(_fullScreenMesh, _blitMaterial);
        _renderContext.End();
    }

    /// <summary>
    /// Releases the intermediate textures and notifies all stages. Called by the pipeline
    /// after the scene texture was recreated at a new size.
    /// </summary>
    public void Resize(uint width, uint height)
    {
        DisposeTemporaries();

        for (int i = 0; i < _stages.Count; i++)
        {
            _stages[i].Resize(width, height);
        }
    }

    private RenderTexture GetTemporary(int index)
    {
        return index == 0 ? _tempA! : _tempB!;
    }

    private void EnsureTemporaries(RenderTexture source)
    {
        if (_tempA != null && _tempB != null
            && _tempA.Width == source.Width && _tempA.Height == source.Height
            && _tempFormat == source.AttachmentLayout.Colors[0].Format)
        {
            return;
        }

        DisposeTemporaries();

        _tempFormat = source.AttachmentLayout.Colors[0].Format;
        _tempLayout ??= _rendering.GraphicsDevice.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(_tempFormat)],
            null,
            "post_process_temporary"));

        _tempA = _rendering.CreateRenderTexture(_tempLayout, source.Width, source.Height, "post_process_temp_a");
        _tempB = _rendering.CreateRenderTexture(_tempLayout, source.Width, source.Height, "post_process_temp_b");
    }

    private void DisposeTemporaries()
    {
        _tempA?.Dispose();
        _tempB?.Dispose();
        _tempA = null;
        _tempB = null;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            for (int i = 0; i < _stages.Count; i++)
            {
                _stages[i].Dispose();
            }
            _stages.Clear();

            DisposeTemporaries();
            _tempLayout?.Dispose();
            _blitMaterial.Dispose();
            _renderContext.Dispose();
        }
    }
}
