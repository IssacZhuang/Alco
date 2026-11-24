using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>Selects how <see cref="RGNode_Bloom"/> composites the accumulated bloom into the frame target.</summary>
public enum BloomCompositeMode : byte
{
    /// <summary>Blends bloom additively into the current color target in place, avoiding a full-resolution copy and preserving compression.</summary>
    InPlaceAdditive,

    /// <summary>Composites scene and bloom in one compute pass into a separate storage target (advances the <see cref="RenderChain"/> when used).</summary>
    SeparateTargetCompute,

    /// <summary>Composites scene and bloom in one graphics pass into a separate render target (safe for swapchain and attachment-only targets).</summary>
    SeparateTargetGraphics,
}

/// <summary>
/// Multi-pass bindless <see cref="IRenderGraphNode"/> implementing sum-of-gaussians HDR bloom with LDS separable gaussian blur
/// and transient <see cref="RenderGraph"/> resource aliasing.
/// </summary>
public sealed unsafe class RGNode_Bloom : IRenderGraphNode
{
    /// <summary>Number of downsample pyramid levels and gaussian stages.</summary>
    public const int StageCount = 6;

    private const float MaxRadius = 31.0f;
    private const uint SetupGroupSize = 8;
    private const uint DownsampleGroupSize = 8;
    private const uint GaussianHGroupSize = 64;
    private const uint GaussianVTileX = 8;
    private const uint GaussianVTileY = 32;
    private const uint BlitFlagSampleScene = 1u << 0;
    private const uint BlitFlagSampleBloom = 1u << 1;

    private const int PassIdSetup = 0;
    private const int PassIdDownsampleBase = 1;
    private const int PassIdGaussianHBase = 10;
    private const int PassIdGaussianVBase = 20;
    private const int PassIdComposite = 30;

    private static readonly BlendState AdditivePreserveAlphaBlend = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.One, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.Zero, Destination = BlendFactor.One, Operation = BlendOp.Add },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct BloomSetupRoot
    {
        public uint Source;
        public uint Dest;
        public Vector2 InvDestSize;
        public uint DestSizeX;
        public uint DestSizeY;
        public float Threshold;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BloomDownsampleRoot
    {
        public uint Source;
        public uint Dest;
        public Vector2 InvSourceSize;
        public Vector2 InvDestSize;
        public uint DestSizeX;
        public uint DestSizeY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BloomGaussianHRoot
    {
        public uint Source;
        public uint Dest;
        public uint SizeX;
        public uint SizeY;
        public float SigmaRadius;
        public int IntegerRadius;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BloomGaussianVRoot
    {
        public uint Source;
        public uint Additive;
        public uint Dest;
        public Vector2 InvDestSize;
        public uint SizeX;
        public uint SizeY;
        public float TintR;
        public float TintG;
        public float TintB;
        public float SigmaRadius;
        public int IntegerRadius;
        public float AdditiveBlend;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BloomBlitRoot
    {
        public uint BloomTex;
        public uint SceneTex;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BloomCompositeRoot
    {
        public uint SceneTex;
        public uint BloomTex;
        public uint Dest;
        public Vector2 InvDestSize;
        public uint DestSizeX;
        public uint DestSizeY;
        public uint HasBloom;
    }

    private struct StagePlan
    {
        public RGTexture Source;
        public RGTexture BlurH;
        public RGTexture AccumPrev;
        public RGTexture AccumOut;
        public Extent3D Extent;
        public float SigmaRadius;
        public int IntegerRadius;
        public Vector3 Tint;
    }

    private readonly RenderingSystem _rendering;
    private readonly RenderChain? _chain;
    private readonly Func<RenderGraphBuilder, RGTexture>? _inputProvider;
    private readonly Func<RenderGraphBuilder, RGTexture>? _outputProvider;

    private readonly Shader _setupShader;
    private readonly Shader _downsampleShader;
    private readonly Shader _gaussianHShader;
    private readonly Shader _gaussianVShader;
    private readonly Shader _compositeComputeShader;
    private readonly Material _blitAdditiveMaterial;
    private readonly Material _blitOpaqueMaterial;

    private readonly string _setupPassName;
    private readonly string _compositePassName;
    private readonly string _compositeOutputName;
    private readonly string[] _pyramidTextureNames = new string[StageCount];
    private readonly string[] _blurHTextureNames = new string[StageCount];
    private readonly string[] _accumTextureNames = new string[StageCount];
    private readonly string[] _downsamplePassNames = new string[StageCount - 1];
    private readonly string[] _gaussianHPassNames = new string[StageCount];
    private readonly string[] _gaussianVPassNames = new string[StageCount];

    private readonly float[] _sizes = [0.3f, 1.0f, 2.0f, 10.0f, 30.0f, 64.0f];
    private readonly Vector3[] _tints = [Vector3.One, Vector3.One, Vector3.One, Vector3.One, Vector3.One, Vector3.One];

    private float _intensity = 1.0f;
    private float _sizeScale = 1.0f;

    // Frame-scoped plan populated in Setup and consumed in Execute without heap allocations.
    private RGTexture _frameInput;
    private RGTexture _frameCompositeTarget;
    private RGTexture _frameAccumLast;
    private bool _frameCompositeIsCompute;
    private bool _frameCompositeIsInPlace;
    private bool _frameCompositeHasBloom;
    private readonly RGTexture[] _framePyramid = new RGTexture[StageCount];
    private readonly Extent3D[] _frameExtents = new Extent3D[StageCount];
    private readonly StagePlan[] _frameStages = new StagePlan[StageCount];

    /// <summary>Creates a bloom post-process node operating on a linear <see cref="RenderChain"/>.</summary>
    /// <param name="rendering">The rendering system owning descriptor heaps and default shaders.</param>
    /// <param name="chain">The render chain whose <see cref="RenderChain.Current"/> texture is bloomed.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    /// <param name="name">Diagnostic node name prefix used for pass and transient labels.</param>
    public RGNode_Bloom(RenderingSystem rendering, RenderChain chain, ShaderSystem? shaders = null, string name = "Bloom")
        : this(rendering, chain ?? throw new ArgumentNullException(nameof(chain)), null, null, shaders, name)
    {
    }

    /// <summary>Creates a bloom post-process node reading from <paramref name="inputProvider"/> and writing to <paramref name="outputProvider"/> (or in place when null).</summary>
    /// <param name="rendering">The rendering system owning descriptor heaps and default shaders.</param>
    /// <param name="inputProvider">Callback resolving the HDR input texture during <see cref="Setup"/>.</param>
    /// <param name="outputProvider">Optional callback resolving the output target during <see cref="Setup"/>; null applies bloom in place onto the input.</param>
    /// <param name="shaders">Optional shader system override; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    /// <param name="name">Diagnostic node name prefix used for pass and transient labels.</param>
    public RGNode_Bloom(
        RenderingSystem rendering,
        Func<RenderGraphBuilder, RGTexture> inputProvider,
        Func<RenderGraphBuilder, RGTexture>? outputProvider = null,
        ShaderSystem? shaders = null,
        string name = "Bloom")
        : this(rendering, null, inputProvider ?? throw new ArgumentNullException(nameof(inputProvider)), outputProvider, shaders, name)
    {
    }

    private RGNode_Bloom(
        RenderingSystem rendering,
        RenderChain? chain,
        Func<RenderGraphBuilder, RGTexture>? inputProvider,
        Func<RenderGraphBuilder, RGTexture>? outputProvider,
        ShaderSystem? shaders,
        string name)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentException.ThrowIfNullOrEmpty(name);

        _rendering = rendering;
        _chain = chain;
        _inputProvider = inputProvider;
        _outputProvider = outputProvider;
        Name = name;

        ShaderSystem shaderSystem = shaders ?? rendering.Shaders;
        shaderSystem.Compiler.LoadModuleFromSource(
            "AlcoRendering_Bloom",
            "AlcoRendering_Bloom.slang",
            LoadEmbeddedShaderSource("AlcoRendering_Bloom.slang"));

        _setupShader = LoadBloomShader(shaderSystem, "BloomSetup", ShaderKind.Compute);
        _downsampleShader = LoadBloomShader(shaderSystem, "BloomDownsample", ShaderKind.Compute);
        _gaussianHShader = LoadBloomShader(shaderSystem, "BloomGaussianH", ShaderKind.Compute);
        _gaussianVShader = LoadBloomShader(shaderSystem, "BloomGaussianV", ShaderKind.Compute);
        _compositeComputeShader = LoadBloomShader(shaderSystem, "BloomComposite", ShaderKind.Compute);
        Shader blitShader = LoadBloomShader(shaderSystem, "BloomBlit", ShaderKind.Graphics);

        VerifyRootLayout<BloomSetupRoot>(_setupShader, "BloomSetupRoot");
        VerifyRootLayout<BloomDownsampleRoot>(_downsampleShader, "BloomDownsampleRoot");
        VerifyRootLayout<BloomGaussianHRoot>(_gaussianHShader, "BloomGaussianHRoot");
        VerifyRootLayout<BloomGaussianVRoot>(_gaussianVShader, "BloomGaussianVRoot");
        VerifyRootLayout<BloomCompositeRoot>(_compositeComputeShader, "BloomCompositeRoot");
        VerifyRootLayout<BloomBlitRoot>(blitShader, "BloomBlitRoot");

        _blitAdditiveMaterial = blitShader.CreateMaterial($"{name}_BlitAdditive");
        _blitAdditiveMaterial.Rasterization = RasterizationState.CullNone;
        _blitAdditiveMaterial.Blend = AdditivePreserveAlphaBlend;

        _blitOpaqueMaterial = blitShader.CreateMaterial($"{name}_BlitOpaque");
        _blitOpaqueMaterial.Rasterization = RasterizationState.CullNone;
        _blitOpaqueMaterial.Blend = BlendState.Opaque;

        _setupPassName = $"{name}_Setup";
        _compositePassName = $"{name}_Composite";
        _compositeOutputName = $"{name}_Output";
        for (int i = 0; i < StageCount; ++i)
        {
            _pyramidTextureNames[i] = $"{name}_L{i}";
            _blurHTextureNames[i] = $"{name}_BlurH{i}";
            _accumTextureNames[i] = $"{name}_Accum{i}";
            _gaussianHPassNames[i] = $"{name}_GaussianH{i}";
            _gaussianVPassNames[i] = $"{name}_GaussianV{i}";
            if (i < StageCount - 1)
            {
                _downsamplePassNames[i] = $"{name}_Downsample{i + 1}";
            }
        }
    }

    /// <summary>The diagnostic node name prefix.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <summary>Brightness cutoff of the setup pass; negative values disable thresholding.</summary>
    public float Threshold { get; set; } = 0.0f;

    /// <summary>Overall bloom intensity multiplier, clamped to non-negative values.</summary>
    public float Intensity
    {
        get => _intensity;
        set => _intensity = Math.Max(value, 0.0f);
    }

    /// <summary>Multiplier applied to every stage's gaussian radius, clamped to <c>[0.0001, 10]</c>.</summary>
    public float SizeScale
    {
        get => _sizeScale;
        set => _sizeScale = Math.Clamp(value, 0.0001f, 10.0f);
    }

    /// <summary>Per-stage gaussian sizes in fine-to-coarse order as percentages of screen width (6 entries).</summary>
    public float[] Sizes => _sizes;

    /// <summary>Per-stage gaussian color tints in fine-to-coarse order (6 entries).</summary>
    public Vector3[] Tints => _tints;

    /// <summary>Controls how bloom is composited onto the scene target.</summary>
    public BloomCompositeMode CompositeMode { get; set; } = BloomCompositeMode.InPlaceAdditive;

    /// <summary>When true, marks the composite pass with <see cref="RenderGraphPassBuilder.ProducesOutput"/> so headless graphs keep it alive without exit barriers.</summary>
    public bool AlwaysProducesOutput { get; set; }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        RGTexture input = _chain != null ? _chain.Current : _inputProvider!(builder);
        if (!input.IsValid)
        {
            return;
        }

        RGTexture target = input;
        if (_chain == null && _outputProvider != null)
        {
            target = _outputProvider(builder);
            if (!target.IsValid)
            {
                return;
            }
        }

        bool isChain = _chain != null;
        bool hasAnyActiveStage = HasAnyActiveStage();

        if (isChain && !hasAnyActiveStage)
        {
            return;
        }

        if (!isChain && target == input && !hasAnyActiveStage)
        {
            return;
        }

        Extent3D inputExtent = builder.GetExtent(input);
        _frameInput = input;

        if (!hasAnyActiveStage)
        {
            // Explicit separate output with zero bloom: single passthrough composite.
            _frameCompositeTarget = target;
            _frameCompositeIsInPlace = false;
            _frameCompositeHasBloom = false;
            _frameAccumLast = default;
            _frameCompositeIsCompute = target != builder.Output && CompositeMode == BloomCompositeMode.SeparateTargetCompute;
            DeclareCompositePass(builder);
            return;
        }

        // 6-level integer right-shift pyramid matching original Bloom resolution steps.
        uint width = Math.Max(inputExtent.X >> 1, 1u);
        uint height = Math.Max(inputExtent.Y >> 1, 1u);
        for (int level = 0; level < StageCount; ++level)
        {
            Extent3D levelExtent = new(width, height, 1u);
            _frameExtents[level] = levelExtent;
            _framePyramid[level] = builder.CreateTexture(new RGTextureDesc
            {
                Name = _pyramidTextureNames[level],
                Extent = levelExtent,
                Format = Format.Rgba16Float,
            });
            width = Math.Max(width >> 1, 1u);
            height = Math.Max(height >> 1, 1u);
        }

        RenderGraphPassBuilder setupPass = builder.AddPass(_setupPassName, PassIdSetup);
        setupPass.Read(input, RGUsage.ComputeRead);
        setupPass.Write(_framePyramid[0], RGUsage.ComputeWrite);

        for (int level = 1; level < StageCount; ++level)
        {
            RenderGraphPassBuilder downPass = builder.AddPass(_downsamplePassNames[level - 1], PassIdDownsampleBase + level);
            downPass.Read(_framePyramid[level - 1], RGUsage.ComputeRead);
            downPass.Write(_framePyramid[level], RGUsage.ComputeWrite);
        }

        float tintScale = Intensity / StageCount;
        RGTexture accumulated = default;
        for (int stage = 0; stage < StageCount; ++stage)
        {
            int sizeIndex = StageCount - 1 - stage;
            float size = _sizes[sizeIndex] * SizeScale;
            if (size <= float.Epsilon)
            {
                continue;
            }

            Extent3D stageExtent = _frameExtents[sizeIndex];
            float radius = stageExtent.X * size * 0.005f;
            float sigmaRadius = Math.Clamp(radius, 0.0001f, MaxRadius);
            int integerRadius = Math.Min(Math.Max((int)MathF.Ceiling(sigmaRadius), 1), (int)MaxRadius);

            RGTexture stageSource = _framePyramid[sizeIndex];
            RGTexture blurH = builder.CreateTexture(new RGTextureDesc
            {
                Name = _blurHTextureNames[stage],
                Extent = stageExtent,
                Format = Format.Rgba16Float,
            });
            RGTexture accumOut = builder.CreateTexture(new RGTextureDesc
            {
                Name = _accumTextureNames[stage],
                Extent = stageExtent,
                Format = Format.Rgba16Float,
            });

            _frameStages[stage] = new StagePlan
            {
                Source = stageSource,
                BlurH = blurH,
                AccumPrev = accumulated,
                AccumOut = accumOut,
                Extent = stageExtent,
                SigmaRadius = sigmaRadius,
                IntegerRadius = integerRadius,
                Tint = _tints[sizeIndex] * tintScale,
            };

            RenderGraphPassBuilder passH = builder.AddPass(_gaussianHPassNames[stage], PassIdGaussianHBase + stage);
            passH.Read(stageSource, RGUsage.ComputeRead);
            passH.Write(blurH, RGUsage.ComputeWrite);

            RenderGraphPassBuilder passV = builder.AddPass(_gaussianVPassNames[stage], PassIdGaussianVBase + stage);
            passV.Read(blurH, RGUsage.ComputeRead);
            if (accumulated.IsValid)
            {
                passV.Read(accumulated, RGUsage.ComputeRead);
            }
            passV.Write(accumOut, RGUsage.ComputeWrite);

            accumulated = accumOut;
        }

        _frameAccumLast = accumulated;
        _frameCompositeHasBloom = accumulated.IsValid;

        if (isChain)
        {
            if (CompositeMode == BloomCompositeMode.InPlaceAdditive)
            {
                _frameCompositeTarget = input;
                _frameCompositeIsInPlace = true;
                _frameCompositeIsCompute = false;
                DeclareCompositePass(builder);
                return;
            }

            RGTexture next = builder.CreateTexture(new RGTextureDesc
            {
                Name = _compositeOutputName,
                Extent = inputExtent,
                Format = builder.GetFormat(input),
            });
            _frameCompositeTarget = next;
            _frameCompositeIsInPlace = false;
            _frameCompositeIsCompute = CompositeMode == BloomCompositeMode.SeparateTargetCompute;
            DeclareCompositePass(builder);
            _chain!.Advance(next);
            return;
        }

        if (target == input)
        {
            _frameCompositeTarget = target;
            _frameCompositeIsInPlace = true;
            _frameCompositeIsCompute = false;
        }
        else
        {
            _frameCompositeTarget = target;
            _frameCompositeIsInPlace = false;
            _frameCompositeIsCompute = target != builder.Output && CompositeMode == BloomCompositeMode.SeparateTargetCompute;
        }

        DeclareCompositePass(builder);
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context, int passId)
    {
        if (passId == PassIdSetup)
        {
            ExecuteSetup(in context);
        }
        else if (passId >= PassIdDownsampleBase && passId < PassIdGaussianHBase)
        {
            ExecuteDownsample(in context, passId - PassIdDownsampleBase);
        }
        else if (passId >= PassIdGaussianHBase && passId < PassIdGaussianVBase)
        {
            ExecuteGaussianH(in context, passId - PassIdGaussianHBase);
        }
        else if (passId >= PassIdGaussianVBase && passId < PassIdComposite)
        {
            ExecuteGaussianV(in context, passId - PassIdGaussianVBase);
        }
        else if (passId == PassIdComposite)
        {
            ExecuteComposite(in context);
        }
    }

    private void ExecuteSetup(in RenderGraphContext context)
    {
        Texture src = context.GetTexture(_frameInput);
        Texture dst = context.GetTexture(_framePyramid[0]);
        Extent3D dstExtent = _frameExtents[0];
        BloomSetupRoot root = new()
        {
            Source = src.SampledIndex,
            Dest = dst.StorageIndex,
            InvDestSize = new Vector2(1.0f / dstExtent.X, 1.0f / dstExtent.Y),
            DestSizeX = dstExtent.X,
            DestSizeY = dstExtent.Y,
            Threshold = Threshold,
        };

        DispatchCompute(
            context.RenderContext,
            _setupShader,
            in root,
            (dstExtent.X + SetupGroupSize - 1u) / SetupGroupSize,
            (dstExtent.Y + SetupGroupSize - 1u) / SetupGroupSize);
    }

    private void ExecuteDownsample(in RenderGraphContext context, int level)
    {
        Texture src = context.GetTexture(_framePyramid[level - 1]);
        Texture dst = context.GetTexture(_framePyramid[level]);
        Extent3D srcExtent = _frameExtents[level - 1];
        Extent3D dstExtent = _frameExtents[level];
        BloomDownsampleRoot root = new()
        {
            Source = src.SampledIndex,
            Dest = dst.StorageIndex,
            InvSourceSize = new Vector2(1.0f / srcExtent.X, 1.0f / srcExtent.Y),
            InvDestSize = new Vector2(1.0f / dstExtent.X, 1.0f / dstExtent.Y),
            DestSizeX = dstExtent.X,
            DestSizeY = dstExtent.Y,
        };

        DispatchCompute(
            context.RenderContext,
            _downsampleShader,
            in root,
            (dstExtent.X + DownsampleGroupSize - 1u) / DownsampleGroupSize,
            (dstExtent.Y + DownsampleGroupSize - 1u) / DownsampleGroupSize);
    }

    private void ExecuteGaussianH(in RenderGraphContext context, int stage)
    {
        ref readonly StagePlan plan = ref _frameStages[stage];
        Texture src = context.GetTexture(plan.Source);
        Texture dst = context.GetTexture(plan.BlurH);
        BloomGaussianHRoot root = new()
        {
            Source = src.SampledIndex,
            Dest = dst.StorageIndex,
            SizeX = plan.Extent.X,
            SizeY = plan.Extent.Y,
            SigmaRadius = plan.SigmaRadius,
            IntegerRadius = plan.IntegerRadius,
        };

        DispatchCompute(
            context.RenderContext,
            _gaussianHShader,
            in root,
            (plan.Extent.X + GaussianHGroupSize - 1u) / GaussianHGroupSize,
            plan.Extent.Y);
    }

    private void ExecuteGaussianV(in RenderGraphContext context, int stage)
    {
        ref readonly StagePlan plan = ref _frameStages[stage];
        Texture src = context.GetTexture(plan.BlurH);
        Texture dst = context.GetTexture(plan.AccumOut);
        uint additiveIndex = plan.AccumPrev.IsValid
            ? context.GetTexture(plan.AccumPrev).SampledIndex
            : _rendering.TextureWhite.SampledIndex;
        BloomGaussianVRoot root = new()
        {
            Source = src.SampledIndex,
            Additive = additiveIndex,
            Dest = dst.StorageIndex,
            InvDestSize = new Vector2(1.0f / plan.Extent.X, 1.0f / plan.Extent.Y),
            SizeX = plan.Extent.X,
            SizeY = plan.Extent.Y,
            TintR = plan.Tint.X,
            TintG = plan.Tint.Y,
            TintB = plan.Tint.Z,
            SigmaRadius = plan.SigmaRadius,
            IntegerRadius = plan.IntegerRadius,
            AdditiveBlend = plan.AccumPrev.IsValid ? 1.0f : 0.0f,
        };

        DispatchCompute(
            context.RenderContext,
            _gaussianVShader,
            in root,
            (plan.Extent.X + GaussianVTileX - 1u) / GaussianVTileX,
            (plan.Extent.Y + GaussianVTileY - 1u) / GaussianVTileY);
    }

    private bool HasAnyActiveStage()
    {
        if (Intensity <= 0.0f)
        {
            return false;
        }

        float scale = SizeScale;
        for (int i = 0; i < StageCount; ++i)
        {
            if (_sizes[i] * scale > float.Epsilon)
            {
                return true;
            }
        }
        return false;
    }

    private void DeclareCompositePass(RenderGraphBuilder builder)
    {
        RenderGraphPassBuilder compositePass = builder.AddPass(_compositePassName, PassIdComposite);
        if (_frameCompositeIsInPlace)
        {
            compositePass.Read(_frameAccumLast, RGUsage.PixelRead);
            compositePass.ReadWrite(_frameCompositeTarget, RGUsage.ColorAttachment);
        }
        else if (_frameCompositeIsCompute)
        {
            compositePass.Read(_frameInput, RGUsage.ComputeRead);
            if (_frameCompositeHasBloom)
            {
                compositePass.Read(_frameAccumLast, RGUsage.ComputeRead);
            }
            compositePass.Write(_frameCompositeTarget, RGUsage.ComputeWrite);
        }
        else
        {
            compositePass.Read(_frameInput, RGUsage.PixelRead);
            if (_frameCompositeHasBloom)
            {
                compositePass.Read(_frameAccumLast, RGUsage.PixelRead);
            }
            compositePass.Write(_frameCompositeTarget, RGUsage.ColorAttachmentWrite);
        }

        if (AlwaysProducesOutput)
        {
            compositePass.ProducesOutput();
        }
    }

    private void ExecuteComposite(in RenderGraphContext context)
    {
        RenderContext rc = context.RenderContext;
        Extent3D targetExtent = context.GetExtent(_frameCompositeTarget);
        if (_frameCompositeIsCompute)
        {
            Texture sceneTex = context.GetTexture(_frameInput);
            Texture dstTex = context.GetTexture(_frameCompositeTarget);
            uint bloomIndex = _frameCompositeHasBloom
                ? context.GetTexture(_frameAccumLast).SampledIndex
                : _rendering.TextureWhite.SampledIndex;
            BloomCompositeRoot root = new()
            {
                SceneTex = sceneTex.SampledIndex,
                BloomTex = bloomIndex,
                Dest = dstTex.StorageIndex,
                InvDestSize = new Vector2(1.0f / targetExtent.X, 1.0f / targetExtent.Y),
                DestSizeX = targetExtent.X,
                DestSizeY = targetExtent.Y,
                HasBloom = _frameCompositeHasBloom ? 1u : 0u,
            };

            DispatchCompute(
                rc,
                _compositeComputeShader,
                in root,
                (targetExtent.X + SetupGroupSize - 1u) / SetupGroupSize,
                (targetExtent.Y + SetupGroupSize - 1u) / SetupGroupSize);
            return;
        }

        uint flags = 0u;
        uint sceneIndex = _rendering.TextureWhite.SampledIndex;
        if (!_frameCompositeIsInPlace)
        {
            flags |= BlitFlagSampleScene;
            sceneIndex = context.GetTexture(_frameInput).SampledIndex;
        }

        uint blitBloomIndex = _rendering.TextureWhite.SampledIndex;
        if (_frameCompositeHasBloom)
        {
            flags |= BlitFlagSampleBloom;
            blitBloomIndex = context.GetTexture(_frameAccumLast).SampledIndex;
        }

        BloomBlitRoot blitRoot = new()
        {
            BloomTex = blitBloomIndex,
            SceneTex = sceneIndex,
            Flags = flags,
        };

        ColorAttachment colorAttachment = new()
        {
            Target = context.GetRenderTarget(_frameCompositeTarget),
            Load = _frameCompositeIsInPlace ? LoadOp.Load : LoadOp.Discard,
            Store = StoreOp.Store,
        };
        RenderingDesc desc = new()
        {
            Colors = MemoryMarshal.CreateReadOnlySpan(ref colorAttachment, 1),
        };

        using RenderContext.RenderScope render = rc.BeginRenderPass(desc);
        render.SetViewport(new Viewport { Width = targetExtent.X, Height = targetExtent.Y });
        render.SetScissor(new Scissor { Width = targetExtent.X, Height = targetExtent.Y });
        Material material = _frameCompositeIsInPlace ? _blitAdditiveMaterial : _blitOpaqueMaterial;
        render.Draw(material, in blitRoot, DepthStencilState.None, 3u);
    }

    private static void DispatchCompute<TRoot>(
        RenderContext rc,
        Shader shader,
        in TRoot rootData,
        uint groupCountX,
        uint groupCountY)
        where TRoot : unmanaged
    {
        if (groupCountX == 0 || groupCountY == 0)
        {
            return;
        }

        using RenderContext.ComputeScope compute = rc.BeginCompute();
        compute.Dispatch(shader, in rootData, new Extent3D(groupCountX, groupCountY, 1u));
    }

    private static Shader LoadBloomShader(ShaderSystem shaderSystem, string moduleName, ShaderKind kind)
    {
        return shaderSystem.GetShaderFromSource(
            moduleName,
            moduleName + ".slang",
            LoadEmbeddedShaderSource(moduleName + ".slang"),
            kind,
            moduleName);
    }

    private static void VerifyRootLayout<TRoot>(Shader shader, string rootTypeName) where TRoot : unmanaged
    {
        if ((uint)sizeof(TRoot) != shader.Root.SizeBytes || !shader.Reflection.MatchesStructLayout<TRoot>(rootTypeName))
        {
            throw new InvalidOperationException(
                $"Shader '{shader.Name}' root struct '{rootTypeName}' ({shader.Root.SizeBytes}B) does not match {typeof(TRoot).Name} ({sizeof(TRoot)}B).");
        }
    }

    private static string LoadEmbeddedShaderSource(string logicalName)
    {
        Assembly assembly = typeof(RGNode_Bloom).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException($"Embedded shader '{logicalName}' was not found in {assembly.FullName}.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
