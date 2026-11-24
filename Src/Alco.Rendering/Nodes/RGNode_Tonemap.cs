using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>Supported tone mapping operators for <see cref="RGNode_Tonemap"/>.</summary>
public enum TonemapType : byte
{
    /// <summary>Linear passthrough without tone mapping, gamma, or dither (preserves source RGBA).</summary>
    Linear = 0,

    /// <summary>ACES fitted filmic curve.</summary>
    ACES = 1,

    /// <summary>Khronos PBR Neutral tone mapping.</summary>
    Neutral = 2,
}

/// <summary>Selects the GPU pass implementation used by <see cref="RGNode_Tonemap"/>.</summary>
public enum TonemapExecutionMode : byte
{
    /// <summary>Uses the compute pass for storage-capable targets and graphics for swapchain, sRGB, or attachment-only targets.</summary>
    Auto = 0,

    /// <summary>Forces the single-pass compute shader.</summary>
    Compute = 1,

    /// <summary>Forces the single-pass fullscreen graphics shader.</summary>
    Graphics = 2,
}

/// <summary>
/// Single-pass bindless <see cref="IRenderGraphNode"/> applying Neutral or ACES HDR tone mapping
/// with compile-time Slang specialization, compute/graphics auto-routing, and optional 8-bit dither.
/// </summary>
public sealed unsafe class RGNode_Tonemap : IRenderGraphNode
{
    private const uint ThreadGroupDim = 8u;

    [StructLayout(LayoutKind.Sequential)]
    private struct TonemapRoot
    {
        public uint Source;
        public uint Dest;
        public uint DestSizeX;
        public uint DestSizeY;
        public Vector2 InvDestSize;
        public float Exposure;
        public float Gamma;
        public float AcesA;
        public float AcesB;
        public float AcesC;
        public float AcesD;
        public float AcesE;
        public float NeutralStartCompression;
        public float NeutralDesaturation;
        public uint Pad0;
    }

    private readonly RenderChain? _chain;
    private readonly Func<RenderGraphBuilder, RGTexture>? _inputProvider;
    private readonly Func<RenderGraphBuilder, RGTexture>? _outputProvider;
    private readonly Material _computeMaterial;
    private readonly Material _graphicsMaterial;
    private readonly string _passName;
    private readonly string _outputTextureName;

    private TonemapType _operator = TonemapType.Neutral;
    private TonemapExecutionMode _executionMode = TonemapExecutionMode.Auto;
    private bool _ditherEnabled = true;
    private Format? _outputFormat;
    private ACESTonemapData _acesData = ACESTonemapData.Default;
    private NeutralTonemapData _neutralData = NeutralTonemapData.Default;

    private TonemapType _computeOperator;
    private bool _computeDither;
    private TonemapType _graphicsOperator;
    private bool _graphicsDither;

    private RGTexture _frameInput;
    private RGTexture _frameTarget;
    private Extent3D _frameTargetExtent;
    private TonemapType _frameEffectiveOperator;
    private bool _frameEffectiveDither;
    private bool _frameUseCompute;

    /// <summary>Creates a tonemap node operating on a linear <see cref="RenderChain"/>.</summary>
    /// <param name="rendering">The rendering system owning descriptor heaps and default shaders.</param>
    /// <param name="chain">The render chain whose <see cref="RenderChain.Current"/> texture is tone-mapped.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    /// <param name="name">Diagnostic node name prefix used for pass and transient labels.</param>
    public RGNode_Tonemap(
        RenderingSystem rendering,
        RenderChain chain,
        ShaderSystem? shaders = null,
        string name = "Tonemap")
        : this(rendering, chain ?? throw new ArgumentNullException(nameof(chain)), null, null, shaders, name)
    {
    }

    /// <summary>Creates a tonemap node reading from <paramref name="inputProvider"/> and writing to <paramref name="outputProvider"/> (or <see cref="RenderGraphBuilder.Output"/> when null).</summary>
    /// <param name="rendering">The rendering system owning descriptor heaps and default shaders.</param>
    /// <param name="inputProvider">Callback resolving the source HDR texture during <see cref="Setup"/>.</param>
    /// <param name="outputProvider">Optional callback resolving the destination target during <see cref="Setup"/>; null writes to <see cref="RenderGraphBuilder.Output"/>.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    /// <param name="name">Diagnostic node name prefix used for pass and transient labels.</param>
    public RGNode_Tonemap(
        RenderingSystem rendering,
        Func<RenderGraphBuilder, RGTexture> inputProvider,
        Func<RenderGraphBuilder, RGTexture>? outputProvider = null,
        ShaderSystem? shaders = null,
        string name = "Tonemap")
        : this(rendering, null, inputProvider ?? throw new ArgumentNullException(nameof(inputProvider)), outputProvider, shaders, name)
    {
    }

    private RGNode_Tonemap(
        RenderingSystem rendering,
        RenderChain? chain,
        Func<RenderGraphBuilder, RGTexture>? inputProvider,
        Func<RenderGraphBuilder, RGTexture>? outputProvider,
        ShaderSystem? shaders,
        string name)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentException.ThrowIfNullOrEmpty(name);

        _chain = chain;
        _inputProvider = inputProvider;
        _outputProvider = outputProvider;
        Name = name;
        _passName = $"{name}_Apply";
        _outputTextureName = $"{name}_Output";

        ShaderSystem shaderSystem = shaders ?? rendering.Shaders;
        shaderSystem.Compiler.LoadModuleFromSource(
            "AlcoRendering_Tonemap",
            "AlcoRendering_Tonemap.slang",
            LoadEmbeddedShaderSource("AlcoRendering_Tonemap.slang"));

        Shader computeShader = LoadTonemapShader(shaderSystem, "TonemapCompute", ShaderKind.Compute);
        Shader graphicsShader = LoadTonemapShader(shaderSystem, "TonemapGraphics", ShaderKind.Graphics);

        int ditherFlag = _ditherEnabled ? 1 : 0;
        _computeMaterial = computeShader.CreateMaterial($"{name}_Compute", (int)_operator, ditherFlag);
        _graphicsMaterial = graphicsShader.CreateMaterial($"{name}_Graphics", (int)_operator, ditherFlag);
        _graphicsMaterial.Rasterization = RasterizationState.CullNone;
        _graphicsMaterial.Blend = BlendState.Opaque;

        _computeOperator = _operator;
        _computeDither = _ditherEnabled;
        _graphicsOperator = _operator;
        _graphicsDither = _ditherEnabled;

        VerifyRootLayout<TonemapRoot>(_computeMaterial, "TonemapRoot");
        VerifyRootLayout<TonemapRoot>(_graphicsMaterial, "TonemapRoot");
    }

    /// <summary>The diagnostic node name prefix.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <summary>When true, skips the pass in <see cref="RenderChain"/> mode or passes source RGBA through unchanged to explicit outputs.</summary>
    public bool Bypass { get; set; }

    /// <summary>The active tone mapping operator; switching selects a pre-specialized shader variant.</summary>
    public TonemapType Operator
    {
        get => _operator;
        set => _operator = value;
    }

    /// <summary>Parameters for the ACES fitted operator.</summary>
    public ACESTonemapData ACESData
    {
        get => _acesData;
        set => _acesData = value;
    }

    /// <summary>Parameters for the Khronos PBR Neutral operator.</summary>
    public NeutralTonemapData NeutralData
    {
        get => _neutralData;
        set => _neutralData = value;
    }

    /// <summary>When true, applies 8-bit triangular IGN output dithering to ACES and Neutral results.</summary>
    public bool DitherEnabled
    {
        get => _ditherEnabled;
        set => _ditherEnabled = value;
    }

    /// <summary>
    /// Optional chain-mode transient output format override; when null, <see cref="TonemapType.ACES"/> and
    /// <see cref="TonemapType.Neutral"/> converge to <see cref="Format.Rgba8Unorm"/> while <see cref="TonemapType.Linear"/> preserves the input format.
    /// </summary>
    public Format? OutputFormat
    {
        get => _outputFormat;
        set => _outputFormat = value;
    }

    /// <summary>Selects whether tonemapping dispatches via compute, fullscreen graphics, or chooses automatically per target.</summary>
    public TonemapExecutionMode ExecutionMode
    {
        get => _executionMode;
        set => _executionMode = value;
    }

    /// <summary>When true, marks the pass with <see cref="RenderGraphPassBuilder.ProducesOutput"/> so headless graphs retain it.</summary>
    public bool AlwaysProducesOutput { get; set; }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        _frameInput = default;
        _frameTarget = default;

        RGTexture input = _chain != null ? _chain.Current : _inputProvider!(builder);
        if (!input.IsValid)
        {
            return;
        }

        bool isChain = _chain != null;
        if (isChain && Bypass && !AlwaysProducesOutput)
        {
            return;
        }

        TonemapType effectiveOperator = Bypass ? TonemapType.Linear : _operator;
        bool effectiveDither = effectiveOperator != TonemapType.Linear && _ditherEnabled;

        RGTexture target;
        if (isChain)
        {
            Extent3D inputExtent = builder.GetExtent(input);
            Format inputFormat = builder.GetFormat(input);
            Format targetFormat = _outputFormat ?? (effectiveOperator == TonemapType.Linear ? inputFormat : Format.Rgba8Unorm);

            target = builder.CreateTexture(new RGTextureDesc
            {
                Name = _outputTextureName,
                Extent = inputExtent,
                Format = targetFormat,
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
                    $"Tonemap node '{Name}' cannot read and write the same texture in place; supply a distinct output target or use RenderChain.");
            }
        }

        bool useCompute = ResolveUseCompute(builder, target);

        _frameInput = input;
        _frameTarget = target;
        _frameTargetExtent = builder.GetExtent(target);
        _frameEffectiveOperator = effectiveOperator;
        _frameEffectiveDither = effectiveDither;
        _frameUseCompute = useCompute;

        RenderGraphPassBuilder pass = builder.AddPass(_passName);
        RGUsage readUsage = useCompute ? RGUsage.ComputeRead : RGUsage.PixelRead;
        RGUsage writeUsage = useCompute ? RGUsage.ComputeWrite : RGUsage.ColorAttachmentWrite;
        pass.Read(input, readUsage);
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
        if (_frameTargetExtent.X == 0 || _frameTargetExtent.Y == 0)
        {
            return;
        }

        EnsureSpecialization(
            _computeMaterial,
            ref _computeOperator,
            ref _computeDither,
            _frameEffectiveOperator,
            _frameEffectiveDither);

        Texture src = context.GetTexture(_frameInput);
        Texture dst = context.GetTexture(_frameTarget);
        TonemapRoot rootData = BuildRoot(src.SampledIndex, dst.StorageIndex);

        RenderContext rc = context.RenderContext;
        Extent3D groupCount = new(
            (_frameTargetExtent.X + ThreadGroupDim - 1u) / ThreadGroupDim,
            (_frameTargetExtent.Y + ThreadGroupDim - 1u) / ThreadGroupDim,
            1u);

        using RenderContext.ComputeScope compute = rc.BeginCompute();
        compute.Dispatch(_computeMaterial, in rootData, groupCount);
    }

    private void ExecuteGraphics(in RenderGraphContext context)
    {
        if (_frameTargetExtent.X == 0 || _frameTargetExtent.Y == 0)
        {
            return;
        }

        EnsureSpecialization(
            _graphicsMaterial,
            ref _graphicsOperator,
            ref _graphicsDither,
            _frameEffectiveOperator,
            _frameEffectiveDither);

        Texture src = context.GetTexture(_frameInput);
        TonemapRoot rootData = BuildRoot(src.SampledIndex, 0u);

        RenderContext rc = context.RenderContext;

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
        render.SetViewport(new Viewport { Width = _frameTargetExtent.X, Height = _frameTargetExtent.Y });
        render.SetScissor(new Scissor { Width = _frameTargetExtent.X, Height = _frameTargetExtent.Y });
        render.Draw(_graphicsMaterial, in rootData, DepthStencilState.None, 3u);
    }

    private TonemapRoot BuildRoot(uint sourceIndex, uint destIndex)
    {
        bool isAces = _frameEffectiveOperator == TonemapType.ACES;
        return new TonemapRoot
        {
            Source = sourceIndex,
            Dest = destIndex,
            DestSizeX = _frameTargetExtent.X,
            DestSizeY = _frameTargetExtent.Y,
            InvDestSize = new Vector2(1.0f / _frameTargetExtent.X, 1.0f / _frameTargetExtent.Y),
            Exposure = isAces ? _acesData.Exposure : _neutralData.Exposure,
            Gamma = isAces ? _acesData.Gamma : _neutralData.Gamma,
            AcesA = _acesData.A,
            AcesB = _acesData.B,
            AcesC = _acesData.C,
            AcesD = _acesData.D,
            AcesE = _acesData.E,
            NeutralStartCompression = _neutralData.StartCompression,
            NeutralDesaturation = _neutralData.Desaturation,
        };
    }

    private bool ResolveUseCompute(RenderGraphBuilder builder, RGTexture target)
    {
        if (_executionMode == TonemapExecutionMode.Graphics)
        {
            return false;
        }

        if (_executionMode == TonemapExecutionMode.Compute)
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

    private static void EnsureSpecialization(
        Material material,
        ref TonemapType currentOperator,
        ref bool currentDither,
        TonemapType targetOperator,
        bool targetDither)
    {
        if (currentOperator == targetOperator && currentDither == targetDither)
        {
            return;
        }

        material.SetSpecializations((int)targetOperator, targetDither ? 1 : 0);
        currentOperator = targetOperator;
        currentDither = targetDither;
    }

    private static Shader LoadTonemapShader(ShaderSystem shaderSystem, string moduleName, ShaderKind kind)
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
        Assembly assembly = typeof(RGNode_Tonemap).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException($"Embedded shader '{logicalName}' was not found in {assembly.FullName}.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
