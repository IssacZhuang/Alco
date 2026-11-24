using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>Selects the edge-walk search depth preset for <see cref="RGNode_FXAA"/>.</summary>
public enum FXAAQuality : byte
{
    /// <summary>4 search steps (up to 15 texels); fastest preset.</summary>
    Low = 0,

    /// <summary>7 search steps (up to 21.5 texels); balanced default.</summary>
    Medium = 1,

    /// <summary>12 search steps (up to 44.5 texels); high quality for shallow edges.</summary>
    High = 2,

    /// <summary>16 search steps (up to 74.5 texels); maximum edge span reach.</summary>
    Ultra = 3,
}

/// <summary>Selects how <see cref="RGNode_FXAA"/> detects anti-aliased edges.</summary>
public enum FXAAMode : byte
{
    /// <summary>Perceptual luma edge detection without depth reads.</summary>
    Luma = 0,

    /// <summary>Hybrid detection where scene depth rescues low-contrast silhouettes while luma handles surface edges.</summary>
    DepthAssisted = 1,

    /// <summary>Anti-aliases only depth-detected geometric silhouettes, leaving surface textures and UI untouched.</summary>
    DepthOnly = 2,
}

/// <summary>Selects the color space assumption used to compute perceptual luma in <see cref="RGNode_FXAA"/>.</summary>
public enum FXAAColorSpace : byte
{
    /// <summary>Gamma-encoded LDR input after tone mapping.</summary>
    Gamma = 0,

    /// <summary>Linear LDR input (such as hardware-decoded sRGB textures).</summary>
    Linear = 1,

    /// <summary>Linear HDR input using Reinhard-compressed perceptual luma.</summary>
    Hdr = 2,
}

/// <summary>Selects the GPU pass implementation used by <see cref="RGNode_FXAA"/>.</summary>
public enum FXAAExecutionMode : byte
{
    /// <summary>Uses the LDS-accelerated compute pass for storage-capable targets and graphics for swapchain/attachment-only targets.</summary>
    Auto = 0,

    /// <summary>Forces the single-pass LDS compute shader.</summary>
    Compute = 1,

    /// <summary>Forces the single-pass fullscreen triangle graphics shader.</summary>
    Graphics = 2,
}

/// <summary>
/// Single-pass bindless <see cref="IRenderGraphNode"/> implementing perceptual-luma and second-order depth-aware FXAA
/// with an LDS-cached compute path and a fullscreen graphics fallback for swapchain targets.
/// </summary>
public sealed unsafe class RGNode_FXAA : IRenderGraphNode
{
    private const uint TileDim = 16u;

    [StructLayout(LayoutKind.Sequential)]
    private struct FxaaRoot
    {
        public Vector2 InvSourceSize;
        public uint SourceSizeX;
        public uint SourceSizeY;
        public uint Source;
        public uint Depth;
        public uint Dest;
        public uint IsPassthrough;
        public float EdgeThreshold;
        public float EdgeThresholdMin;
        public float SubpixQuality;
        public float DepthThresholdMin;
        public float DepthThresholdRel;
        public float ColorVisibilityFloor;
    }

    private readonly RenderingSystem _rendering;
    private readonly RenderChain? _chain;
    private readonly Func<RenderGraphBuilder, RGTexture>? _inputProvider;
    private readonly Func<RenderGraphBuilder, RGTexture>? _outputProvider;
    private readonly Func<RenderGraphBuilder, RGTexture>? _depthProvider;
    private readonly Material _computeMaterial;
    private readonly Material _graphicsMaterial;
    private readonly string _passName;
    private readonly string _outputTextureName;

    private FXAAQuality _quality = FXAAQuality.Medium;
    private FXAAMode _mode = FXAAMode.Luma;
    private FXAAColorSpace _colorSpace = FXAAColorSpace.Gamma;
    private FXAAExecutionMode _executionMode = FXAAExecutionMode.Auto;
    private float _threshold = 0.125f;
    private float _thresholdMin = 0.0312f;
    private float _subpix = 0.75f;
    private float _depthThresholdMin = 1.0f / 4096.0f;
    private float _depthThresholdRel = 1.0f / 64.0f;
    private float _colorVisibilityFloor = 1.0f / 64.0f;

    private FXAAQuality _computeQuality = FXAAQuality.Medium;
    private FXAAMode _computeMode = FXAAMode.Luma;
    private FXAAColorSpace _computeColorSpace = FXAAColorSpace.Gamma;
    private FXAAQuality _graphicsQuality = FXAAQuality.Medium;
    private FXAAMode _graphicsMode = FXAAMode.Luma;
    private FXAAColorSpace _graphicsColorSpace = FXAAColorSpace.Gamma;

    private RGTexture _frameInput;
    private RGTexture _frameDepth;
    private RGTexture _frameTarget;
    private Extent3D _frameInputExtent;
    private FXAAMode _frameEffectiveMode;
    private bool _frameUseCompute;
    private bool _framePassthrough;

    /// <summary>Creates an FXAA node operating on a linear <see cref="RenderChain"/>.</summary>
    /// <param name="rendering">The rendering system owning descriptor heaps and default shaders.</param>
    /// <param name="chain">The render chain whose <see cref="RenderChain.Current"/> texture is anti-aliased.</param>
    /// <param name="depthProvider">Optional callback resolving the scene depth texture during <see cref="Setup"/> for depth-aware modes.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    /// <param name="name">Diagnostic node name prefix used for pass and transient labels.</param>
    public RGNode_FXAA(
        RenderingSystem rendering,
        RenderChain chain,
        Func<RenderGraphBuilder, RGTexture>? depthProvider = null,
        ShaderSystem? shaders = null,
        string name = "FXAA")
        : this(rendering, chain ?? throw new ArgumentNullException(nameof(chain)), null, null, depthProvider, shaders, name)
    {
    }

    /// <summary>Creates an FXAA node reading from <paramref name="inputProvider"/> and writing to <paramref name="outputProvider"/> (or <see cref="RenderGraphBuilder.Output"/> when null).</summary>
    /// <param name="rendering">The rendering system owning descriptor heaps and default shaders.</param>
    /// <param name="inputProvider">Callback resolving the source color texture during <see cref="Setup"/>.</param>
    /// <param name="outputProvider">Optional callback resolving the destination target during <see cref="Setup"/>; null writes to <see cref="RenderGraphBuilder.Output"/>.</param>
    /// <param name="depthProvider">Optional callback resolving the scene depth texture during <see cref="Setup"/> for depth-aware modes.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    /// <param name="name">Diagnostic node name prefix used for pass and transient labels.</param>
    public RGNode_FXAA(
        RenderingSystem rendering,
        Func<RenderGraphBuilder, RGTexture> inputProvider,
        Func<RenderGraphBuilder, RGTexture>? outputProvider = null,
        Func<RenderGraphBuilder, RGTexture>? depthProvider = null,
        ShaderSystem? shaders = null,
        string name = "FXAA")
        : this(rendering, null, inputProvider ?? throw new ArgumentNullException(nameof(inputProvider)), outputProvider, depthProvider, shaders, name)
    {
    }

    private RGNode_FXAA(
        RenderingSystem rendering,
        RenderChain? chain,
        Func<RenderGraphBuilder, RGTexture>? inputProvider,
        Func<RenderGraphBuilder, RGTexture>? outputProvider,
        Func<RenderGraphBuilder, RGTexture>? depthProvider,
        ShaderSystem? shaders,
        string name)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentException.ThrowIfNullOrEmpty(name);

        _rendering = rendering;
        _chain = chain;
        _inputProvider = inputProvider;
        _outputProvider = outputProvider;
        _depthProvider = depthProvider;
        Name = name;
        _passName = $"{name}_Apply";
        _outputTextureName = $"{name}_Output";

        ShaderSystem shaderSystem = shaders ?? rendering.Shaders;
        shaderSystem.Compiler.LoadModuleFromSource(
            "AlcoRendering_FXAA",
            "AlcoRendering_FXAA.slang",
            LoadEmbeddedShaderSource("AlcoRendering_FXAA.slang"));

        Shader computeShader = LoadFxaaShader(shaderSystem, "FXAACompute", ShaderKind.Compute);
        Shader graphicsShader = LoadFxaaShader(shaderSystem, "FXAAGraphics", ShaderKind.Graphics);

        _computeMaterial = computeShader.CreateMaterial($"{name}_Compute", (int)_quality, (int)_mode, (int)_colorSpace);
        _graphicsMaterial = graphicsShader.CreateMaterial($"{name}_Graphics", (int)_quality, (int)_mode, (int)_colorSpace);
        _graphicsMaterial.Rasterization = RasterizationState.CullNone;
        _graphicsMaterial.Blend = BlendState.Opaque;

        VerifyRootLayout<FxaaRoot>(_computeMaterial, "FxaaRoot");
        VerifyRootLayout<FxaaRoot>(_graphicsMaterial, "FxaaRoot");
    }

    /// <summary>The diagnostic node name prefix.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <summary>When true, bypasses anti-aliasing and copies the input unchanged when writing to an explicit output target.</summary>
    public bool Bypass { get; set; }

    /// <summary>Edge-walk search preset controlling span reach and quality.</summary>
    public FXAAQuality Quality
    {
        get => _quality;
        set => _quality = value;
    }

    /// <summary>Edge-detection mode; depth-aware modes require a depth provider supplied at construction.</summary>
    /// <exception cref="InvalidOperationException">Thrown when selecting a depth-aware mode without a depth provider.</exception>
    public FXAAMode Mode
    {
        get => _mode;
        set
        {
            if (value != FXAAMode.Luma && _depthProvider == null)
            {
                throw new InvalidOperationException(
                    $"FXAA mode '{value}' samples scene depth, but '{Name}' was constructed without a depth provider.");
            }
            _mode = value;
        }
    }

    /// <summary>Color space assumption used when converting source RGB to perceptual luma.</summary>
    public FXAAColorSpace ColorSpace
    {
        get => _colorSpace;
        set => _colorSpace = value;
    }

    /// <summary>Selects whether FXAA dispatches via LDS compute, fullscreen graphics, or chooses automatically per target.</summary>
    public FXAAExecutionMode ExecutionMode
    {
        get => _executionMode;
        set => _executionMode = value;
    }

    /// <summary>Relative local contrast threshold in <c>[0.063, 0.333]</c>; lower values process more edges.</summary>
    public float Threshold
    {
        get => _threshold;
        set => _threshold = Math.Clamp(value, 0.063f, 0.333f);
    }

    /// <summary>Minimum absolute luma contrast floor in <c>[0.015, 0.125]</c> that suppresses dark-region noise.</summary>
    public float ThresholdMin
    {
        get => _thresholdMin;
        set => _thresholdMin = Math.Clamp(value, 0.015f, 0.125f);
    }

    /// <summary>Directional subpixel aliasing attenuation in <c>[0, 1]</c>; higher values smooth isolated subpixel aliasing.</summary>
    public float Subpix
    {
        get => _subpix;
        set => _subpix = Math.Clamp(value, 0.0f, 1.0f);
    }

    /// <summary>Absolute minimum planar depth discontinuity in <c>[0, 0.01]</c> for depth-aware modes.</summary>
    public float DepthThresholdMin
    {
        get => _depthThresholdMin;
        set => _depthThresholdMin = Math.Clamp(value, 0.0f, 0.01f);
    }

    /// <summary>Relative depth discontinuity threshold in <c>[0, 0.25]</c> scaled by center depth magnitude.</summary>
    public float DepthThresholdRel
    {
        get => _depthThresholdRel;
        set => _depthThresholdRel = Math.Clamp(value, 0.0f, 0.25f);
    }

    /// <summary>Minimum visible luma range in <c>[0, 0.125]</c> required before depth-detected edges trigger blending.</summary>
    public float ColorVisibilityFloor
    {
        get => _colorVisibilityFloor;
        set => _colorVisibilityFloor = Math.Clamp(value, 0.0f, 0.125f);
    }

    /// <summary>When true, marks the FXAA pass with <see cref="RenderGraphPassBuilder.ProducesOutput"/> so headless graphs retain it.</summary>
    public bool AlwaysProducesOutput { get; set; }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        RGTexture input = _chain != null ? _chain.Current : _inputProvider!(builder);
        if (!input.IsValid)
        {
            return;
        }

        bool isChain = _chain != null;
        bool passthrough = Bypass;
        RGTexture depth = default;
        FXAAMode effectiveMode = _mode;

        if (!passthrough && effectiveMode != FXAAMode.Luma)
        {
            depth = _depthProvider!(builder);
            if (!depth.IsValid)
            {
                if (effectiveMode == FXAAMode.DepthAssisted)
                {
                    effectiveMode = FXAAMode.Luma;
                }
                else
                {
                    effectiveMode = FXAAMode.Luma;
                    passthrough = true;
                }
            }
        }

        if (isChain && passthrough && !AlwaysProducesOutput)
        {
            return;
        }

        Extent3D inputExtent = builder.GetExtent(input);
        Format inputFormat = builder.GetFormat(input);

        RGTexture target;
        if (isChain)
        {
            target = builder.CreateTexture(new RGTextureDesc
            {
                Name = _outputTextureName,
                Extent = inputExtent,
                Format = inputFormat,
            });
            _chain!.Advance(target);
        }
        else
        {
            target = _outputProvider != null ? _outputProvider(builder) : builder.Output;
            if (!target.IsValid)
            {
                return;
            }

            if (target == input)
            {
                throw new InvalidOperationException(
                    $"FXAA node '{Name}' cannot read and write the same texture in place; supply a distinct output target or use RenderChain.");
            }
        }

        bool useCompute = ResolveUseCompute(builder, target);

        _frameInput = input;
        _frameDepth = depth;
        _frameTarget = target;
        _frameInputExtent = inputExtent;
        _frameEffectiveMode = effectiveMode;
        _frameUseCompute = useCompute;
        _framePassthrough = passthrough;

        RenderGraphPassBuilder pass = builder.AddPass(_passName);
        RGUsage readUsage = useCompute ? RGUsage.ComputeRead : RGUsage.PixelRead;
        RGUsage writeUsage = useCompute ? RGUsage.ComputeWrite : RGUsage.ColorAttachmentWrite;
        pass.Read(input, readUsage);
        if (!passthrough && effectiveMode != FXAAMode.Luma && depth.IsValid)
        {
            pass.Read(depth, readUsage);
        }
        pass.Write(target, writeUsage);

        if (AlwaysProducesOutput)
        {
            pass.ProducesOutput();
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context, int passId)
    {
        if (!_frameInput.IsValid || !_frameTarget.IsValid)
        {
            return;
        }

        if (_frameUseCompute)
        {
            ExecuteCompute(in context);
        }
        else
        {
            ExecuteGraphics(in context);
        }
    }

    private void ExecuteCompute(in RenderGraphContext context)
    {
        if (_frameInputExtent.X == 0 || _frameInputExtent.Y == 0)
        {
            return;
        }

        EnsureSpecialization(
            _computeMaterial,
            ref _computeQuality,
            ref _computeMode,
            ref _computeColorSpace,
            _frameEffectiveMode);

        Texture src = context.GetTexture(_frameInput);
        Texture dst = context.GetTexture(_frameTarget);
        FxaaRoot rootData = BuildRoot(in context, src.SampledIndex, dst.StorageIndex);

        RenderContext rc = context.RenderContext;
        Extent3D groupCount = new(
            (_frameInputExtent.X + TileDim - 1u) / TileDim,
            (_frameInputExtent.Y + TileDim - 1u) / TileDim,
            1u);

        using RenderContext.ComputeScope compute = rc.BeginCompute();
        compute.Dispatch(_computeMaterial, in rootData, groupCount);
    }

    private void ExecuteGraphics(in RenderGraphContext context)
    {
        EnsureSpecialization(
            _graphicsMaterial,
            ref _graphicsQuality,
            ref _graphicsMode,
            ref _graphicsColorSpace,
            _frameEffectiveMode);

        Texture src = context.GetTexture(_frameInput);
        FxaaRoot rootData = BuildRoot(in context, src.SampledIndex, 0u);

        RenderContext rc = context.RenderContext;
        Extent3D targetExtent = context.GetExtent(_frameTarget);

        ColorAttachment colorAttachment = new()
        {
            Target = context.GetRenderTarget(_frameTarget),
            Load = LoadOp.Discard,
            Store = StoreOp.Store,
        };
        RenderingDesc desc = new()
        {
            Colors = MemoryMarshal.CreateReadOnlySpan(ref colorAttachment, 1),
        };

        using RenderContext.RenderScope render = rc.BeginRenderPass(desc);
        render.SetViewport(new Viewport { Width = targetExtent.X, Height = targetExtent.Y });
        render.SetScissor(new Scissor { Width = targetExtent.X, Height = targetExtent.Y });
        render.Draw(_graphicsMaterial, in rootData, DepthStencilState.None, 3u);
    }

    private FxaaRoot BuildRoot(in RenderGraphContext context, uint sourceIndex, uint destIndex)
    {
        uint depthIndex = !_framePassthrough && _frameEffectiveMode != FXAAMode.Luma && _frameDepth.IsValid
            ? context.GetTexture(_frameDepth).SampledIndex
            : _rendering.TextureWhite.SampledIndex;

        return new FxaaRoot
        {
            InvSourceSize = new Vector2(1.0f / _frameInputExtent.X, 1.0f / _frameInputExtent.Y),
            SourceSizeX = _frameInputExtent.X,
            SourceSizeY = _frameInputExtent.Y,
            Source = sourceIndex,
            Depth = depthIndex,
            Dest = destIndex,
            IsPassthrough = _framePassthrough ? 1u : 0u,
            EdgeThreshold = _threshold,
            EdgeThresholdMin = _thresholdMin,
            SubpixQuality = _subpix,
            DepthThresholdMin = _depthThresholdMin,
            DepthThresholdRel = _depthThresholdRel,
            ColorVisibilityFloor = _colorVisibilityFloor,
        };
    }

    private bool ResolveUseCompute(RenderGraphBuilder builder, RGTexture target)
    {
        if (_executionMode == FXAAExecutionMode.Graphics)
        {
            return false;
        }

        if (_executionMode == FXAAExecutionMode.Compute)
        {
            return true;
        }

        if (target == builder.Output)
        {
            return false;
        }

        Format format = builder.GetFormat(target);
        if (format is Format.Rgba8Srgb or Format.Bgra8Srgb)
        {
            return false;
        }

        return builder.SupportsUsage(target, RGUsage.ComputeWrite);
    }

    private void EnsureSpecialization(
        Material material,
        ref FXAAQuality currentQuality,
        ref FXAAMode currentMode,
        ref FXAAColorSpace currentColorSpace,
        FXAAMode targetMode)
    {
        if (currentQuality == _quality && currentMode == targetMode && currentColorSpace == _colorSpace)
        {
            return;
        }

        material.SetSpecializations((int)_quality, (int)targetMode, (int)_colorSpace);
        currentQuality = _quality;
        currentMode = targetMode;
        currentColorSpace = _colorSpace;
    }

    private static Shader LoadFxaaShader(ShaderSystem shaderSystem, string moduleName, ShaderKind kind)
    {
        return shaderSystem.GetShaderFromSource(
            moduleName,
            moduleName + ".slang",
            LoadEmbeddedShaderSource(moduleName + ".slang"),
            kind,
            moduleName);
    }

    private static void VerifyRootLayout<TRoot>(Material material, string rootTypeName) where TRoot : unmanaged
    {
        if ((uint)sizeof(TRoot) != material.Root.SizeBytes || !material.Reflection.MatchesStructLayout<TRoot>(rootTypeName))
        {
            throw new InvalidOperationException(
                $"Shader '{material.Shader.Name}' root struct '{rootTypeName}' ({material.Root.SizeBytes}B) does not match {typeof(TRoot).Name} ({sizeof(TRoot)}B).");
        }
    }

    private static string LoadEmbeddedShaderSource(string logicalName)
    {
        Assembly assembly = typeof(RGNode_FXAA).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException($"Embedded shader '{logicalName}' was not found in {assembly.FullName}.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
