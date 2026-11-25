using System.Numerics;
using Alco.Graphics;

namespace Alco.GUI;

/// <summary>RenderGraph node that prepares, uploads, and draws a <see cref="Canvas"/> with transient <c>S8Uint</c> stencil masking.</summary>
public sealed class CanvasRenderGraphNode : RenderGraphNode
{
    private readonly Canvas _canvas;
    private readonly RenderChain? _chain;
    private readonly LoadOp _colorLoadOp;
    private readonly Vector4 _clearColor;
    private readonly string _stencilTargetName;

    private RGTexture _colorTarget;
    private RGTexture _stencilTarget;

    /// <summary>Creates a render graph overlay node driving <paramref name="canvas"/>.</summary>
    /// <param name="canvas">The UI canvas to prepare and draw each frame.</param>
    /// <param name="chain">Optional linear render chain; when null the pass targets <see cref="RenderGraphBuilder.Output"/>.</param>
    /// <param name="colorLoadOp">Color load operation (<see cref="LoadOp.Load"/> compositing onto scene vs <see cref="LoadOp.Clear"/>).</param>
    /// <param name="clearColor">Clear color used when <paramref name="colorLoadOp"/> is <see cref="LoadOp.Clear"/>.</param>
    /// <param name="name">Diagnostic render pass name.</param>
    public CanvasRenderGraphNode(
        Canvas canvas,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        string name = "AlcoGUI")
        : base(name)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _stencilTargetName = $"{Name}_Stencil";
    }

    /// <summary>The canvas driven by this node.</summary>
    public Canvas Canvas => _canvas;

    /// <summary>Optional callback invoked immediately before the canvas prepares and draws.</summary>
    public Action? BeforeExecute { get; set; }

    /// <summary>Optional callback invoked immediately after the canvas finishes preparing and drawing.</summary>
    public Action? AfterExecute { get; set; }

    /// <inheritdoc/>
    protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
    {
        _colorTarget = _chain != null ? _chain.Current : builder.Output;
        if (_colorLoadOp == LoadOp.Load)
            pass.ReadWrite(_colorTarget, RGUsage.ColorAttachment);
        else
            pass.Write(_colorTarget, RGUsage.ColorAttachmentWrite);

        _stencilTarget = builder.CreateTexture(new RGTextureDesc
        {
            Name = _stencilTargetName,
            Format = Format.S8Uint,
            Usage = TextureUsage.DepthStencilAttachment,
        });
        pass.Write(_stencilTarget, RGUsage.DepthStencil);
    }

    /// <inheritdoc/>
    protected override void ExecutePass(in RenderGraphContext context)
    {
        BeforeExecute?.Invoke();
        try
        {
            _canvas.PrepareRender(context.DeltaTime);
            _canvas.Draw(
                context.RenderContext,
                context.GetRenderTarget(_colorTarget),
                context.GetRenderTarget(_stencilTarget),
                _colorLoadOp,
                _clearColor);
        }
        finally
        {
            AfterExecute?.Invoke();
        }
    }
}
