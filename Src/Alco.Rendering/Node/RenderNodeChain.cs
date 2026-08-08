
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A linear chain of forward render nodes owned by a pipeline (<see cref="ForwardPipeline"/>
/// or the resolve stage of a <see cref="PBRDeferredPipeline"/>). The chain executes the
/// enabled nodes in registration order against a content texture:
/// <list type="bullet">
/// <item>Content nodes (<see cref="IForwardRenderNode"/>) draw into the texture holding
/// the content produced so far — the pipeline's content texture, or a chain temporary
/// once a processor has run.</item>
/// <item>Content processors (<see cref="IContentProcessorNode"/>) transform that content,
/// reading one texture and writing the other of the chain-owned ping-pong pair.</item>
/// <item>After the last node the chain blits the final content texture into the
/// destination. Processors never touch the destination directly: their contract is
/// purely texture-to-texture.</item>
/// </list>
/// The schedule is recomputed every frame, so toggling <see cref="IRenderNode.IsEnabled"/>
/// or reordering nodes needs no other bookkeeping. When the final destination is null
/// (minimized or headless view), content nodes still render into the content texture and
/// all processors are skipped.
/// <br/>The chain takes ownership of the registered nodes: nodes implementing
/// <see cref="System.IDisposable"/> are disposed with the chain.
/// </summary>
public sealed class RenderNodeChain : AutoDisposable
{
    private readonly RenderingSystem _rendering;
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Material _blitMaterial;
    private readonly List<IRenderNode> _nodes = new();

    private GPUAttachmentLayout? _tempLayout;
    private PixelFormat _tempFormat;
    private RenderTexture? _tempA;
    private RenderTexture? _tempB;

    /// <summary>
    /// The registered nodes, in execution (registration) order.
    /// </summary>
    public IReadOnlyList<IRenderNode> Nodes => _nodes;

    /// <summary>
    /// Whether the chain has any enabled content-producing node (a node that is not a
    /// <see cref="IContentProcessorNode"/>). A deferred pipeline uses this to decide
    /// whether its forward resolve has anything to composite at all.
    /// </summary>
    public bool HasEnabledContentNodes
    {
        get
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].IsEnabled && _nodes[i] is not IContentProcessorNode)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Creates an empty chain.
    /// </summary>
    /// <param name="rendering">The rendering system for creating GPU resources.</param>
    /// <param name="blitShader">The shader used for plain copies between textures.</param>
    public RenderNodeChain(RenderingSystem rendering, Shader blitShader)
    {
        _rendering = rendering;
        _renderContext = rendering.CreateRenderContext();
        _fullScreenMesh = rendering.MeshFullScreen;
        _blitMaterial = rendering.CreateMaterial(blitShader);
    }

    /// <summary>
    /// Registers a node at the end of the chain. The chain takes ownership and disposes
    /// the node (when <see cref="System.IDisposable"/>) with itself.
    /// </summary>
    public void Use(IRenderNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is not (IForwardRenderNode or IContentProcessorNode))
        {
            throw new ArgumentException(
                $"Render node {node.GetType().Name} implements neither {nameof(IForwardRenderNode)} nor {nameof(IContentProcessorNode)} and cannot run in a forward chain.",
                nameof(node));
        }
        _nodes.Add(node);
    }

    /// <summary>
    /// Removes a node previously added via <see cref="Use"/>. The node is not disposed.
    /// </summary>
    public bool Remove(IRenderNode node)
    {
        return _nodes.Remove(node);
    }

    /// <summary>
    /// Gets the first node of the given type, or null when the chain has none.
    /// </summary>
    public T? Get<T>() where T : class, IRenderNode
    {
        for (int i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i] is T node)
            {
                return node;
            }
        }
        return null;
    }

    /// <summary>
    /// Executes the enabled nodes against <paramref name="content"/>, then blits the
    /// final content texture into <paramref name="destination"/>.
    /// </summary>
    /// <param name="content">The pipeline's content texture (scene or forward-resolved).</param>
    /// <param name="destination">The final output frame buffer (e.g. the swapchain). When
    /// null, content nodes still render into the content texture and all processors are
    /// skipped (minimized or headless view).</param>
    public void Execute(RenderTexture content, GPUFrameBuffer? destination)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (destination == null)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].IsEnabled && _nodes[i] is IForwardRenderNode contentNode)
                {
                    contentNode.OnRenderForward(content.FrameBuffer, content.AttachmentLayout);
                }
            }
            return;
        }

        int processorCount = 0;
        for (int i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i].IsEnabled && _nodes[i] is IContentProcessorNode)
            {
                processorCount++;
            }
        }

        if (processorCount > 0)
        {
            EnsureTemporaries(content);
        }

        RenderTexture current = content;
        int toggle = 0;

        for (int i = 0; i < _nodes.Count; i++)
        {
            IRenderNode node = _nodes[i];
            if (!node.IsEnabled)
            {
                continue;
            }

            if (node is IContentProcessorNode processor)
            {
                RenderTexture temporary = GetTemporary(toggle);
                processor.OnRenderForward(current, temporary);
                current = temporary;
                toggle ^= 1;
            }
            else
            {
                ((IForwardRenderNode)node).OnRenderForward(current.FrameBuffer, current.AttachmentLayout);
            }
        }

        Blit(current, destination);
    }

    /// <summary>
    /// Releases the intermediate textures and notifies all nodes. Called by the pipeline
    /// after the content texture was recreated at a new size.
    /// </summary>
    public void Resize(uint width, uint height)
    {
        DisposeTemporaries();

        for (int i = 0; i < _nodes.Count; i++)
        {
            _nodes[i].Resize(width, height);
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="destination"/> unchanged.
    /// </summary>
    private void Blit(RenderTexture source, GPUFrameBuffer destination)
    {
        _blitMaterial.SetRenderTexture(ShaderResourceId.Texture, source);
        _renderContext.Begin(destination);
        _renderContext.Draw(_fullScreenMesh, _blitMaterial);
        _renderContext.End();
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
            "render_node_chain_temporary"));

        _tempA = _rendering.CreateRenderTexture(_tempLayout, source.Width, source.Height, "render_node_chain_temp_a");
        _tempB = _rendering.CreateRenderTexture(_tempLayout, source.Width, source.Height, "render_node_chain_temp_b");
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
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i] is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _nodes.Clear();

            DisposeTemporaries();
            _tempLayout?.Dispose();
            _blitMaterial.Dispose();
            _renderContext.Dispose();
        }
    }
}
