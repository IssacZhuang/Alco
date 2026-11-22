using System.Numerics;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;

namespace Alco.Effects;

/// <summary>RenderGraph compute pass driving <see cref="GpuParticleSystem2D.RecordSimulation"/>.</summary>
public sealed class ParticleSimulateNode2D : RenderGraphNode
{
    private readonly GpuParticleSystem2D _particles;

    /// <summary>Creates a simulation graph node for <paramref name="particles"/>.</summary>
    public ParticleSimulateNode2D(GpuParticleSystem2D particles, string name = "ParticleSimulate2D")
        : base(name)
    {
        _particles = particles ?? throw new ArgumentNullException(nameof(particles));
    }

    /// <inheritdoc/>
    protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
    {
        pass.ProducesOutput();
    }

    /// <inheritdoc/>
    protected override void ExecutePass(in RenderGraphContext context)
    {
        _particles.RecordSimulation(context.RenderContext, context.DeltaTime);
    }
}

/// <summary>RenderGraph compute pass driving <see cref="GpuParticleSystem3D.RecordSimulation"/>.</summary>
public sealed class ParticleSimulateNode3D : RenderGraphNode
{
    private readonly GpuParticleSystem3D _particles;

    /// <summary>Creates a simulation graph node for <paramref name="particles"/>.</summary>
    public ParticleSimulateNode3D(GpuParticleSystem3D particles, string name = "ParticleSimulate3D")
        : base(name)
    {
        _particles = particles ?? throw new ArgumentNullException(nameof(particles));
    }

    /// <inheritdoc/>
    protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
    {
        pass.ProducesOutput();
    }

    /// <inheritdoc/>
    protected override void ExecutePass(in RenderGraphContext context)
    {
        _particles.RecordSimulation(context.RenderContext, context.DeltaTime);
    }
}

/// <summary>RenderGraph graphics pass drawing <see cref="GpuParticleSystem2D"/> batches onto a target.</summary>
public sealed class ParticleRenderNode2D : RenderGraphNode
{
    private readonly GpuParticleSystem2D _particles;
    private readonly Func<ICamera> _cameraProvider;
    private readonly RenderChain? _chain;
    private readonly LoadOp _colorLoadOp;
    private readonly Vector4 _clearColor;
    private readonly Func<Texture?>? _depthTextureProvider;
    private readonly Func<RGTexture>? _depthHandleProvider;

    private RGTexture _colorTarget;
    private RGTexture _depthTarget;

    /// <summary>Creates a 2D particle render node bound to a fixed camera instance.</summary>
    public ParticleRenderNode2D(
        GpuParticleSystem2D particles,
        ICamera camera,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "ParticleRender2D")
        : this(particles, () => camera ?? throw new ArgumentNullException(nameof(camera)), chain, colorLoadOp, clearColor, depthTextureProvider, depthHandleProvider, name)
    {
    }

    /// <summary>Creates a 2D particle render node with a per-frame camera delegate.</summary>
    public ParticleRenderNode2D(
        GpuParticleSystem2D particles,
        Func<ICamera> cameraProvider,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "ParticleRender2D")
        : base(name)
    {
        _particles = particles ?? throw new ArgumentNullException(nameof(particles));
        _cameraProvider = cameraProvider ?? throw new ArgumentNullException(nameof(cameraProvider));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _depthTextureProvider = depthTextureProvider;
        _depthHandleProvider = depthHandleProvider;
    }

    /// <inheritdoc/>
    protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
    {
        _colorTarget = _chain != null ? _chain.Current : builder.Output;
        EffectNodeHelpers.DeclareColorTarget(pass, _colorTarget, _colorLoadOp);
        _depthTarget = EffectNodeHelpers.ResolveAndReadDepth(builder, pass, _depthTextureProvider, _depthHandleProvider);
    }

    /// <inheritdoc/>
    protected override void ExecutePass(in RenderGraphContext context)
    {
        EffectNodeHelpers.ExecuteRenderPass(
            in context,
            _colorTarget,
            _depthTarget,
            _colorLoadOp,
            _clearColor,
            (ctx, scope) => _particles.Render(ctx, scope, _cameraProvider()));
    }
}

/// <summary>RenderGraph graphics pass drawing <see cref="GpuParticleSystem3D"/> batches onto a target.</summary>
public sealed class ParticleRenderNode3D : RenderGraphNode
{
    private readonly GpuParticleSystem3D _particles;
    private readonly Func<ICamera> _cameraProvider;
    private readonly RenderChain? _chain;
    private readonly LoadOp _colorLoadOp;
    private readonly Vector4 _clearColor;
    private readonly Func<Texture?>? _depthTextureProvider;
    private readonly Func<RGTexture>? _depthHandleProvider;

    private RGTexture _colorTarget;
    private RGTexture _depthTarget;

    /// <summary>Creates a 3D particle render node bound to a fixed camera instance.</summary>
    public ParticleRenderNode3D(
        GpuParticleSystem3D particles,
        ICamera camera,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "ParticleRender3D")
        : this(particles, () => camera ?? throw new ArgumentNullException(nameof(camera)), chain, colorLoadOp, clearColor, depthTextureProvider, depthHandleProvider, name)
    {
    }

    /// <summary>Creates a 3D particle render node with a per-frame camera delegate.</summary>
    public ParticleRenderNode3D(
        GpuParticleSystem3D particles,
        Func<ICamera> cameraProvider,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "ParticleRender3D")
        : base(name)
    {
        _particles = particles ?? throw new ArgumentNullException(nameof(particles));
        _cameraProvider = cameraProvider ?? throw new ArgumentNullException(nameof(cameraProvider));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _depthTextureProvider = depthTextureProvider;
        _depthHandleProvider = depthHandleProvider;
    }

    /// <inheritdoc/>
    protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
    {
        _colorTarget = _chain != null ? _chain.Current : builder.Output;
        EffectNodeHelpers.DeclareColorTarget(pass, _colorTarget, _colorLoadOp);
        _depthTarget = EffectNodeHelpers.ResolveAndReadDepth(builder, pass, _depthTextureProvider, _depthHandleProvider);
    }

    /// <inheritdoc/>
    protected override void ExecutePass(in RenderGraphContext context)
    {
        EffectNodeHelpers.ExecuteRenderPass(
            in context,
            _colorTarget,
            _depthTarget,
            _colorLoadOp,
            _clearColor,
            (ctx, scope) => _particles.Render(ctx, scope, _cameraProvider()));
    }
}

/// <summary>RenderGraph graphics pass preparing and drawing <see cref="GpuTrailSystem2D"/> ribbons.</summary>
public sealed class TrailRenderNode2D : RenderGraphNode
{
    private readonly GpuTrailSystem2D _trails;
    private readonly Func<ICamera> _cameraProvider;
    private readonly RenderChain? _chain;
    private readonly LoadOp _colorLoadOp;
    private readonly Vector4 _clearColor;
    private readonly Func<Texture?>? _depthTextureProvider;
    private readonly Func<RGTexture>? _depthHandleProvider;

    private RGTexture _colorTarget;
    private RGTexture _depthTarget;

    /// <summary>Creates a 2D trail render node bound to a fixed camera instance.</summary>
    public TrailRenderNode2D(
        GpuTrailSystem2D trails,
        ICamera camera,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "TrailRender2D")
        : this(trails, () => camera ?? throw new ArgumentNullException(nameof(camera)), chain, colorLoadOp, clearColor, depthTextureProvider, depthHandleProvider, name)
    {
    }

    /// <summary>Creates a 2D trail render node with a per-frame camera delegate.</summary>
    public TrailRenderNode2D(
        GpuTrailSystem2D trails,
        Func<ICamera> cameraProvider,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "TrailRender2D")
        : base(name)
    {
        _trails = trails ?? throw new ArgumentNullException(nameof(trails));
        _cameraProvider = cameraProvider ?? throw new ArgumentNullException(nameof(cameraProvider));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _depthTextureProvider = depthTextureProvider;
        _depthHandleProvider = depthHandleProvider;
    }

    /// <inheritdoc/>
    protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
    {
        _colorTarget = _chain != null ? _chain.Current : builder.Output;
        EffectNodeHelpers.DeclareColorTarget(pass, _colorTarget, _colorLoadOp);
        _depthTarget = EffectNodeHelpers.ResolveAndReadDepth(builder, pass, _depthTextureProvider, _depthHandleProvider);
    }

    /// <inheritdoc/>
    protected override void ExecutePass(in RenderGraphContext context)
    {
        _trails.PrepareRender(context.RenderContext);
        EffectNodeHelpers.ExecuteRenderPass(
            in context,
            _colorTarget,
            _depthTarget,
            _colorLoadOp,
            _clearColor,
            (ctx, scope) => _trails.Render(ctx, scope, _cameraProvider()));
    }
}

/// <summary>RenderGraph graphics pass preparing and drawing <see cref="GpuTrailSystem3D"/> ribbons.</summary>
public sealed class TrailRenderNode3D : RenderGraphNode
{
    private readonly GpuTrailSystem3D _trails;
    private readonly Func<ICamera> _cameraProvider;
    private readonly RenderChain? _chain;
    private readonly LoadOp _colorLoadOp;
    private readonly Vector4 _clearColor;
    private readonly Func<Texture?>? _depthTextureProvider;
    private readonly Func<RGTexture>? _depthHandleProvider;

    private RGTexture _colorTarget;
    private RGTexture _depthTarget;

    /// <summary>Creates a 3D trail render node bound to a fixed camera instance.</summary>
    public TrailRenderNode3D(
        GpuTrailSystem3D trails,
        ICamera camera,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "TrailRender3D")
        : this(trails, () => camera ?? throw new ArgumentNullException(nameof(camera)), chain, colorLoadOp, clearColor, depthTextureProvider, depthHandleProvider, name)
    {
    }

    /// <summary>Creates a 3D trail render node with a per-frame camera delegate.</summary>
    public TrailRenderNode3D(
        GpuTrailSystem3D trails,
        Func<ICamera> cameraProvider,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "TrailRender3D")
        : base(name)
    {
        _trails = trails ?? throw new ArgumentNullException(nameof(trails));
        _cameraProvider = cameraProvider ?? throw new ArgumentNullException(nameof(cameraProvider));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _depthTextureProvider = depthTextureProvider;
        _depthHandleProvider = depthHandleProvider;
    }

    /// <inheritdoc/>
    protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
    {
        _colorTarget = _chain != null ? _chain.Current : builder.Output;
        EffectNodeHelpers.DeclareColorTarget(pass, _colorTarget, _colorLoadOp);
        _depthTarget = EffectNodeHelpers.ResolveAndReadDepth(builder, pass, _depthTextureProvider, _depthHandleProvider);
    }

    /// <inheritdoc/>
    protected override void ExecutePass(in RenderGraphContext context)
    {
        _trails.PrepareRender(context.RenderContext);
        EffectNodeHelpers.ExecuteRenderPass(
            in context,
            _colorTarget,
            _depthTarget,
            _colorLoadOp,
            _clearColor,
            (ctx, scope) => _trails.Render(ctx, scope, _cameraProvider()));
    }
}

internal static class EffectNodeHelpers
{
    public static void DeclareColorTarget(RenderGraphPassBuilder pass, RGTexture colorTarget, LoadOp colorLoadOp)
    {
        if (colorLoadOp == LoadOp.Load)
            pass.ReadWrite(colorTarget, RGUsage.ColorAttachment);
        else
            pass.Write(colorTarget, RGUsage.ColorAttachmentWrite);
    }

    public static RGTexture ResolveAndReadDepth(
        RenderGraphBuilder builder,
        RenderGraphPassBuilder pass,
        Func<Texture?>? depthTextureProvider,
        Func<RGTexture>? depthHandleProvider)
    {
        RGTexture depth = depthHandleProvider != null ? depthHandleProvider() : default;
        if (!depth.IsValid && depthTextureProvider != null)
        {
            Texture? texture = depthTextureProvider();
            if (texture != null)
                depth = builder.ImportTexture(texture, "EffectsDepth", RGUsage.DepthStencilRead);
        }

        if (depth.IsValid)
            pass.Read(depth, RGUsage.DepthStencilRead);
        return depth;
    }

    public static void ExecuteRenderPass(
        in RenderGraphContext context,
        RGTexture colorTarget,
        RGTexture depthTarget,
        LoadOp colorLoadOp,
        Vector4 clearColor,
        Action<RenderContext, RenderContext.RenderScope> draw)
    {
        RenderContext rc = context.RenderContext;
        Format colorFormat = context.GetFormat(colorTarget);
        GPURenderView colorView = context.GetRenderView(colorTarget);
        bool hasDepth = depthTarget.IsValid;
        Format depthFormat = hasDepth ? context.GetFormat(depthTarget) : Format.Undefined;
        GPURenderView? depthView = hasDepth ? context.GetRenderView(depthTarget) : null;

        var desc = new RenderingDesc
        {
            Colors =
            [
                new ColorAttachment
                {
                    RenderView = colorView,
                    Load = colorLoadOp,
                    Store = StoreOp.Store,
                    Clear = clearColor,
                },
            ],
            Depth = hasDepth
                ? new DepthAttachment { RenderView = depthView, Load = LoadOp.Load, Store = StoreOp.Store }
                : default,
        };

        var signature = new RenderPassSignature([colorFormat], depthFormat);
        Extent2D vp = context.Viewport;
        using RenderContext.RenderScope render = rc.BeginRenderPass(desc, signature);
        render.SetViewport(new Viewport { Width = vp.X, Height = vp.Y });
        render.SetScissor(new Scissor { Width = vp.X, Height = vp.Y });
        draw(rc, render);
    }
}
