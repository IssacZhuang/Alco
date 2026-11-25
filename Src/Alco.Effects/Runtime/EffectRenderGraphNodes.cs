using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Effects;

/// <summary>RenderGraph compute pass driving <see cref="GpuParticleSystem2D.RecordSimulation"/>.</summary>
public sealed class RGNode_ParticleSimulate2D : RenderGraphNode
{
    private readonly GpuParticleSystem2D _particles;

    /// <summary>Creates a simulation graph node for <paramref name="particles"/>.</summary>
    public RGNode_ParticleSimulate2D(GpuParticleSystem2D particles, string name = "ParticleSimulate2D")
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
public sealed class RGNode_ParticleSimulate3D : RenderGraphNode
{
    private readonly GpuParticleSystem3D _particles;

    /// <summary>Creates a simulation graph node for <paramref name="particles"/>.</summary>
    public RGNode_ParticleSimulate3D(GpuParticleSystem3D particles, string name = "ParticleSimulate3D")
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
public sealed class RGNode_ParticleRender2D : RenderGraphNode
{
    private readonly GpuParticleSystem2D _particles;
    private readonly ICamera? _camera;
    private readonly Func<ICamera>? _cameraProvider;
    private readonly RenderChain? _chain;
    private readonly LoadOp _colorLoadOp;
    private readonly Vector4 _clearColor;
    private readonly Func<Texture?>? _depthTextureProvider;
    private readonly Func<RGTexture>? _depthHandleProvider;
    private readonly bool? _writesDepth;

    private RGTexture _colorTarget;
    private RGTexture _depthTarget;

    /// <summary>Creates a 2D particle render node bound to a fixed camera instance.</summary>
    public RGNode_ParticleRender2D(
        GpuParticleSystem2D particles,
        ICamera camera,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "ParticleRender2D",
        bool? writesDepth = null)
        : base(name)
    {
        _particles = particles ?? throw new ArgumentNullException(nameof(particles));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _depthTextureProvider = depthTextureProvider;
        _depthHandleProvider = depthHandleProvider;
        _writesDepth = writesDepth;
    }

    /// <summary>Creates a 2D particle render node with a per-frame camera delegate.</summary>
    public RGNode_ParticleRender2D(
        GpuParticleSystem2D particles,
        Func<ICamera> cameraProvider,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "ParticleRender2D",
        bool? writesDepth = null)
        : base(name)
    {
        _particles = particles ?? throw new ArgumentNullException(nameof(particles));
        _cameraProvider = cameraProvider ?? throw new ArgumentNullException(nameof(cameraProvider));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _depthTextureProvider = depthTextureProvider;
        _depthHandleProvider = depthHandleProvider;
        _writesDepth = writesDepth;
    }

    /// <inheritdoc/>
    protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
    {
        _colorTarget = _chain != null ? _chain.Current : builder.Output;
        if (!_colorTarget.IsValid)
        {
            _depthTarget = default;
            return;
        }
        EffectNodeUtility.DeclareColorTarget(pass, _colorTarget, _colorLoadOp);
        _depthTarget = EffectNodeUtility.ResolveAndDeclareDepth(
            builder, pass, _depthTextureProvider, _depthHandleProvider,
            _writesDepth ?? _particles.HasDepthWritingEffects);
    }

    /// <inheritdoc/>
    protected override void ExecutePass(in RenderGraphContext context)
    {
        if (!_colorTarget.IsValid)
            return;
        ICamera camera = _camera ?? _cameraProvider!();
        using RenderContext.RenderScope render = EffectNodeUtility.BeginRenderPass(
            in context, _colorTarget, _depthTarget, _colorLoadOp, _clearColor);
        _particles.Render(context.RenderContext, render, camera);
    }
}

/// <summary>RenderGraph graphics pass drawing <see cref="GpuParticleSystem3D"/> batches onto a target.</summary>
public sealed class RGNode_ParticleRender3D : RenderGraphNode
{
    private readonly GpuParticleSystem3D _particles;
    private readonly ICamera? _camera;
    private readonly Func<ICamera>? _cameraProvider;
    private readonly RenderChain? _chain;
    private readonly LoadOp _colorLoadOp;
    private readonly Vector4 _clearColor;
    private readonly Func<Texture?>? _depthTextureProvider;
    private readonly Func<RGTexture>? _depthHandleProvider;
    private readonly bool? _writesDepth;

    private RGTexture _colorTarget;
    private RGTexture _depthTarget;

    /// <summary>Creates a 3D particle render node bound to a fixed camera instance.</summary>
    public RGNode_ParticleRender3D(
        GpuParticleSystem3D particles,
        ICamera camera,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "ParticleRender3D",
        bool? writesDepth = null)
        : base(name)
    {
        _particles = particles ?? throw new ArgumentNullException(nameof(particles));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _depthTextureProvider = depthTextureProvider;
        _depthHandleProvider = depthHandleProvider;
        _writesDepth = writesDepth;
    }

    /// <summary>Creates a 3D particle render node with a per-frame camera delegate.</summary>
    public RGNode_ParticleRender3D(
        GpuParticleSystem3D particles,
        Func<ICamera> cameraProvider,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "ParticleRender3D",
        bool? writesDepth = null)
        : base(name)
    {
        _particles = particles ?? throw new ArgumentNullException(nameof(particles));
        _cameraProvider = cameraProvider ?? throw new ArgumentNullException(nameof(cameraProvider));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _depthTextureProvider = depthTextureProvider;
        _depthHandleProvider = depthHandleProvider;
        _writesDepth = writesDepth;
    }

    /// <inheritdoc/>
    protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
    {
        _colorTarget = _chain != null ? _chain.Current : builder.Output;
        if (!_colorTarget.IsValid)
        {
            _depthTarget = default;
            return;
        }
        EffectNodeUtility.DeclareColorTarget(pass, _colorTarget, _colorLoadOp);
        _depthTarget = EffectNodeUtility.ResolveAndDeclareDepth(
            builder, pass, _depthTextureProvider, _depthHandleProvider,
            _writesDepth ?? _particles.HasDepthWritingEffects);
    }

    /// <inheritdoc/>
    protected override void ExecutePass(in RenderGraphContext context)
    {
        if (!_colorTarget.IsValid)
            return;
        ICamera camera = _camera ?? _cameraProvider!();
        using RenderContext.RenderScope render = EffectNodeUtility.BeginRenderPass(
            in context, _colorTarget, _depthTarget, _colorLoadOp, _clearColor);
        _particles.Render(context.RenderContext, render, camera);
    }
}

/// <summary>RenderGraph graphics pass preparing and drawing <see cref="GpuTrailSystem2D"/> ribbons.</summary>
public sealed class RGNode_TrailRender2D : RenderGraphNode
{
    private readonly GpuTrailSystem2D _trails;
    private readonly ICamera? _camera;
    private readonly Func<ICamera>? _cameraProvider;
    private readonly RenderChain? _chain;
    private readonly LoadOp _colorLoadOp;
    private readonly Vector4 _clearColor;
    private readonly Func<Texture?>? _depthTextureProvider;
    private readonly Func<RGTexture>? _depthHandleProvider;
    private readonly bool? _writesDepth;

    private RGTexture _colorTarget;
    private RGTexture _depthTarget;

    /// <summary>Creates a 2D trail render node bound to a fixed camera instance.</summary>
    public RGNode_TrailRender2D(
        GpuTrailSystem2D trails,
        ICamera camera,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "TrailRender2D",
        bool? writesDepth = null)
        : base(name)
    {
        _trails = trails ?? throw new ArgumentNullException(nameof(trails));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _depthTextureProvider = depthTextureProvider;
        _depthHandleProvider = depthHandleProvider;
        _writesDepth = writesDepth;
    }

    /// <summary>Creates a 2D trail render node with a per-frame camera delegate.</summary>
    public RGNode_TrailRender2D(
        GpuTrailSystem2D trails,
        Func<ICamera> cameraProvider,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "TrailRender2D",
        bool? writesDepth = null)
        : base(name)
    {
        _trails = trails ?? throw new ArgumentNullException(nameof(trails));
        _cameraProvider = cameraProvider ?? throw new ArgumentNullException(nameof(cameraProvider));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _depthTextureProvider = depthTextureProvider;
        _depthHandleProvider = depthHandleProvider;
        _writesDepth = writesDepth;
    }

    /// <inheritdoc/>
    protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
    {
        _colorTarget = _chain != null ? _chain.Current : builder.Output;
        if (!_colorTarget.IsValid)
        {
            _depthTarget = default;
            return;
        }
        EffectNodeUtility.DeclareColorTarget(pass, _colorTarget, _colorLoadOp);
        _depthTarget = EffectNodeUtility.ResolveAndDeclareDepth(
            builder, pass, _depthTextureProvider, _depthHandleProvider,
            _writesDepth ?? _trails.HasDepthWritingEffects);
    }

    /// <inheritdoc/>
    protected override void ExecutePass(in RenderGraphContext context)
    {
        if (!_colorTarget.IsValid)
            return;
        _trails.PrepareRender(context.RenderContext);
        ICamera camera = _camera ?? _cameraProvider!();
        using RenderContext.RenderScope render = EffectNodeUtility.BeginRenderPass(
            in context, _colorTarget, _depthTarget, _colorLoadOp, _clearColor);
        _trails.Render(context.RenderContext, render, camera);
    }
}

/// <summary>RenderGraph graphics pass preparing and drawing <see cref="GpuTrailSystem3D"/> ribbons.</summary>
public sealed class RGNode_TrailRender3D : RenderGraphNode
{
    private readonly GpuTrailSystem3D _trails;
    private readonly ICamera? _camera;
    private readonly Func<ICamera>? _cameraProvider;
    private readonly RenderChain? _chain;
    private readonly LoadOp _colorLoadOp;
    private readonly Vector4 _clearColor;
    private readonly Func<Texture?>? _depthTextureProvider;
    private readonly Func<RGTexture>? _depthHandleProvider;
    private readonly bool? _writesDepth;

    private RGTexture _colorTarget;
    private RGTexture _depthTarget;

    /// <summary>Creates a 3D trail render node bound to a fixed camera instance.</summary>
    public RGNode_TrailRender3D(
        GpuTrailSystem3D trails,
        ICamera camera,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "TrailRender3D",
        bool? writesDepth = null)
        : base(name)
    {
        _trails = trails ?? throw new ArgumentNullException(nameof(trails));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _depthTextureProvider = depthTextureProvider;
        _depthHandleProvider = depthHandleProvider;
        _writesDepth = writesDepth;
    }

    /// <summary>Creates a 3D trail render node with a per-frame camera delegate.</summary>
    public RGNode_TrailRender3D(
        GpuTrailSystem3D trails,
        Func<ICamera> cameraProvider,
        RenderChain? chain = null,
        LoadOp colorLoadOp = LoadOp.Load,
        Vector4 clearColor = default,
        Func<Texture?>? depthTextureProvider = null,
        Func<RGTexture>? depthHandleProvider = null,
        string name = "TrailRender3D",
        bool? writesDepth = null)
        : base(name)
    {
        _trails = trails ?? throw new ArgumentNullException(nameof(trails));
        _cameraProvider = cameraProvider ?? throw new ArgumentNullException(nameof(cameraProvider));
        _chain = chain;
        _colorLoadOp = colorLoadOp;
        _clearColor = clearColor;
        _depthTextureProvider = depthTextureProvider;
        _depthHandleProvider = depthHandleProvider;
        _writesDepth = writesDepth;
    }

    /// <inheritdoc/>
    protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
    {
        _colorTarget = _chain != null ? _chain.Current : builder.Output;
        if (!_colorTarget.IsValid)
        {
            _depthTarget = default;
            return;
        }
        EffectNodeUtility.DeclareColorTarget(pass, _colorTarget, _colorLoadOp);
        _depthTarget = EffectNodeUtility.ResolveAndDeclareDepth(
            builder, pass, _depthTextureProvider, _depthHandleProvider,
            _writesDepth ?? _trails.HasDepthWritingEffects);
    }

    /// <inheritdoc/>
    protected override void ExecutePass(in RenderGraphContext context)
    {
        if (!_colorTarget.IsValid)
            return;
        _trails.PrepareRender(context.RenderContext);
        ICamera camera = _camera ?? _cameraProvider!();
        using RenderContext.RenderScope render = EffectNodeUtility.BeginRenderPass(
            in context, _colorTarget, _depthTarget, _colorLoadOp, _clearColor);
        _trails.Render(context.RenderContext, render, camera);
    }
}

internal static class EffectNodeUtility
{
    public static void DeclareColorTarget(RenderGraphPassBuilder pass, RGTexture colorTarget, LoadOp colorLoadOp)
    {
        if (colorLoadOp == LoadOp.Load)
            pass.ReadWrite(colorTarget, RGUsage.ColorAttachment);
        else
            pass.Write(colorTarget, RGUsage.ColorAttachment);
    }

    public static RGTexture ResolveAndDeclareDepth(
        RenderGraphBuilder builder,
        RenderGraphPassBuilder pass,
        Func<Texture?>? depthTextureProvider,
        Func<RGTexture>? depthHandleProvider,
        bool writesDepth)
    {
        RGTexture depth = depthHandleProvider != null ? depthHandleProvider() : default;
        if (!depth.IsValid && depthTextureProvider != null)
        {
            Texture? texture = depthTextureProvider();
            if (texture != null)
                depth = builder.ImportTexture(texture, "EffectsDepth");
        }

        if (depth.IsValid)
        {
            if (writesDepth)
                pass.ReadWrite(depth, RGUsage.DepthStencil);
            else
                pass.Read(depth, RGUsage.DepthStencilRead);
        }
        return depth;
    }

    public static RenderContext.RenderScope BeginRenderPass(
        in RenderGraphContext context,
        RGTexture colorTarget,
        RGTexture depthTarget,
        LoadOp colorLoadOp,
        Vector4 clearColor)
    {
        RenderContext rc = context.RenderContext;
        ColorAttachment colorAttachment = new()
        {
            Target = context.GetRenderTarget(colorTarget),
            Load = colorLoadOp,
            Store = StoreOp.Store,
            Clear = clearColor,
        };
        bool hasDepth = depthTarget.IsValid;
        var desc = new RenderingDesc
        {
            Colors = MemoryMarshal.CreateReadOnlySpan(ref colorAttachment, 1),
            Depth = hasDepth
                ? new DepthAttachment { Target = context.GetRenderTarget(depthTarget), Load = LoadOp.Load, Store = StoreOp.Store }
                : default,
        };

        Extent2D vp = context.Viewport;
        RenderContext.RenderScope render = rc.BeginRenderPass(desc);
        render.SetViewport(new Viewport { Width = vp.X, Height = vp.Y });
        render.SetScissor(new Scissor { Width = vp.X, Height = vp.Y });
        return render;
    }
}
