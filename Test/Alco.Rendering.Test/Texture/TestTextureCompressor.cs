using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;
using Buffer = Alco.Graphics.Buffer;

namespace Alco.Rendering.Test;

/// <summary>
/// Slang target compilation checks, multi-backend hardware BC1-BC7 decode round-trip tests, sRGB/linear view tests,
/// edge clamping, mip compression, sentinel offset guards, and zero-allocation checks for <see cref="TextureCompressor"/>.
/// </summary>
[TestFixture]
public unsafe class TestTextureCompressor
{
    private const string DecodeVerifyShaderSource = """
        #language slang 2025
        module TextureCompressionDecodeVerify;

        import Core;

        [GPURoot]
        public struct DecodeVerifyRoot
        {
            public GPUBufferView Output;
            public GPUTextureId NativeView;
            public GPUTextureId UnormView;
            public uint2 Extent;
        };

        [shader("compute")]
        [numthreads(8, 8, 1)]
        void mainCS(uint3 thread : SV_DispatchThreadID)
        {
            DecodeVerifyRoot root = gpu_load_root<DecodeVerifyRoot>();
            if (any(thread.xy >= root.Extent))
            {
                return;
            }
            Texture2D<float4> nativeTex = gpu_texture<Texture2D<float4>>(root.NativeView.index);
            Texture2D<float4> unormTex = gpu_texture<Texture2D<float4>>(root.UnormView.index);
            uint pixel = thread.y * root.Extent.x + thread.x;
            uint count = root.Extent.x * root.Extent.y;
            gpu_store<float4>(root.Output, pixel, nativeTex.Load(int3(thread.xy, 0)));
            gpu_store<float4>(root.Output, count + pixel, unormTex.Load(int3(thread.xy, 0)));
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct DecodeVerifyRoot
    {
        public GPUBufferView Output;
        public uint NativeView;
        public uint UnormView;
        public Extent2D Extent;
    }

    private static readonly string[] PassModules =
    [
        "TextureCompressBc1",
        "TextureCompressBc2",
        "TextureCompressBc3",
        "TextureCompressBc4",
        "TextureCompressBc4Snorm",
        "TextureCompressBc5",
        "TextureCompressBc7",
    ];

    private static readonly Format[] AllBcFormats =
    [
        Format.Bc1Unorm,
        Format.Bc1Srgb,
        Format.Bc2Unorm,
        Format.Bc2Srgb,
        Format.Bc3Unorm,
        Format.Bc3Srgb,
        Format.Bc4Unorm,
        Format.Bc4Snorm,
        Format.Bc5Rg,
        Format.Bc7Unorm,
        Format.Bc7Srgb,
    ];

    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.MetalSource)]
    [TestCase(ShaderTarget.Dxil)]
    public void CompressionShaders_CompileAndMatchRootLayout_AcrossAllTargetsAndSpecializations(ShaderTarget target)
    {
        if (!ShaderCompiler.IsTargetSupported(target))
        {
            Assert.Ignore($"Shader target {target} is unsupported on this host.");
            return;
        }

        using ShaderCompiler compiler = new(target);
        RegisterLib(compiler, "AlcoRendering_TextureCompression");
        RegisterLib(compiler, "AlcoRendering_TextureCompressionLegacy");
        RegisterLib(compiler, "AlcoRendering_TextureCompressionBc7");

        foreach (string moduleName in PassModules)
        {
            ShaderModule module = compiler.LoadModuleFromSource(
                moduleName,
                moduleName + ".slang",
                LoadEmbeddedShader(moduleName + ".slang"));

            for (int srgbFlag = 0; srgbFlag <= 1; ++srgbFlag)
            {
                CompiledShader compiled = module.Compile(
                    ShaderPipelineKind.Compute,
                    new ShaderCompileOptions
                    {
                        SpecializationArgs = [srgbFlag.ToString()],
                    });
                Assert.That(compiled.Stages.Count, Is.EqualTo(1), $"{moduleName}<{srgbFlag}> on {target}");
                Assert.That(compiled.Reflection.Root, Is.Not.Null, $"{moduleName}<{srgbFlag}> on {target}");
                Assert.That(compiled.Reflection.Root!.SizeBytes, Is.EqualTo(24u), $"{moduleName}<{srgbFlag}> root size");
            }
        }
    }

    [Test]
    public void StaticHelpers_SizingPaddingAndSupportChecks_MatchBcGeometry()
    {
        Assert.That(TextureCompressor.IsSupportedFormat(Format.Rgba8Unorm), Is.False);
        foreach (Format format in AllBcFormats)
        {
            Assert.That(TextureCompressor.IsSupportedFormat(format), Is.True);
        }

        Assert.That(TextureCompressor.GetPaddedExtent(new Extent2D(1, 1)), Is.EqualTo(new Extent2D(4, 4)));
        Assert.That(TextureCompressor.GetPaddedExtent(new Extent2D(7, 5)), Is.EqualTo(new Extent2D(8, 8)));
        Assert.That(TextureCompressor.GetPaddedExtent(new Extent2D(36, 37)), Is.EqualTo(new Extent2D(36, 40)));

        Assert.That(TextureCompressor.GetCompressedSizeBytes(Format.Bc1Unorm, new Extent2D(1, 1)), Is.EqualTo(8UL));
        Assert.That(TextureCompressor.GetCompressedSizeBytes(Format.Bc1Unorm, new Extent2D(7, 5)), Is.EqualTo(32UL));
        Assert.That(TextureCompressor.GetCompressedSizeBytes(Format.Bc7Srgb, new Extent2D(36, 37)), Is.EqualTo(9UL * 10UL * 16UL));
        Assert.That(TextureCompressor.GetTotalCompressedSizeBytes(Format.Bc3Unorm, new Extent2D(16, 16), 0, 3),
            Is.EqualTo(16UL * 16UL + 4UL * 16UL + 1UL * 16UL));
    }

    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [TestCase(GraphicsBackend.Dx12)]
    [TestCase(GraphicsBackend.Metal)]
    public void Gpu_AllFormats_OddDimensions_MipOffsets_SrgbPaths_And_ZeroAllocRecord(GraphicsBackend backend)
    {
        if (!TryCreateHost(backend, out RenderingTestHost? host))
            return;

        using (host!)
        {
            RenderingSystem rendering = host.RenderingSystem;
            TextureCompressor compressor = new(rendering);
            Shader decodeShader = rendering.Shaders.GetShaderFromSource(
                "TextureCompressionDecodeVerify",
                "TextureCompressionDecodeVerify.slang",
                DecodeVerifyShaderSource,
                ShaderKind.Compute,
                "TextureCompressionDecodeVerify");

            // 1. All 11 BC formats at non-4-multiple extent (36x37 -> padded 36x40) with mip=1 and sentinel-guarded buffer offset.
            foreach (Format format in AllBcFormats)
            {
                if (!TextureCompressor.CanCompressOnGpu(rendering, format))
                    continue;

                VerifyFormatRoundTrip(rendering, compressor, decodeShader, format, new Extent2D(36, 37), sourceMip: 1);
            }

            // 2. Small non-multiple-of-4 edge-clamping cases (1x1, 2x2, 7x5).
            Extent2D[] oddExtents = [new Extent2D(1, 1), new Extent2D(2, 2), new Extent2D(7, 5)];
            foreach (Extent2D odd in oddExtents)
            {
                VerifyFormatRoundTrip(rendering, compressor, decodeShader, Format.Bc3Srgb, odd, sourceMip: 0);
                VerifyFormatRoundTrip(rendering, compressor, decodeShader, Format.Bc7Srgb, odd, sourceMip: 0);
            }

            // 3. Rgba8Srgb MutableFormat (code-domain zero conversion) vs Linear source (ConvertLinearToSrgb = 1).
            VerifySrgbColorSpaceModes(rendering, compressor, decodeShader);

            // 4. Multi-mip pack via CompressMipChainToBytes.
            VerifyMultiMipChainBytes(rendering, compressor);

            // 5. Steady-state zero-GC verification on Record.
            VerifyZeroAllocationRecord(rendering, compressor);
        }
    }

    private static void VerifyFormatRoundTrip(
        RenderingSystem rendering,
        TextureCompressor compressor,
        Shader decodeShader,
        Format format,
        Extent2D extent,
        uint sourceMip)
    {
        bool signed = format == Format.Bc4Snorm;
        Format sourceFormat = signed ? Format.Rgba32Float : Format.Rgba8Unorm;
        uint sourceWidth = extent.X << (int)sourceMip;
        uint sourceHeight = extent.Y << (int)sourceMip;
        Vector4[] expected = BuildTestPattern(extent, signed);
        if (!signed)
        {
            for (int i = 0; i < expected.Length; ++i)
            {
                expected[i] = new Vector4(
                    MathF.Round(Math.Clamp(expected[i].X, 0.0f, 1.0f) * 255.0f) / 255.0f,
                    MathF.Round(Math.Clamp(expected[i].Y, 0.0f, 1.0f) * 255.0f) / 255.0f,
                    MathF.Round(Math.Clamp(expected[i].Z, 0.0f, 1.0f) * 255.0f) / 255.0f,
                    MathF.Round(Math.Clamp(expected[i].W, 0.0f, 1.0f) * 255.0f) / 255.0f);
            }
        }

        Texture source = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(sourceWidth, sourceHeight, 1),
            MipLevels = sourceMip + 1,
            Format = sourceFormat,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        Extent2D padded = TextureCompressor.GetPaddedExtent(extent);
        Texture destination = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(padded.X, padded.Y, 1),
            Format = format,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            MutableFormat = IsSrgb(format),
        });

        const ulong guardBytes = 64;
        ulong encodedBytes = TextureCompressor.GetCompressedSizeBytes(format, extent);
        byte[] sentinelInit = new byte[checked((int)(guardBytes * 2 + encodedBytes))];
        sentinelInit.AsSpan().Fill(0xAA);
        Buffer blockBuffer = rendering.CreateBuffer<byte>(sentinelInit);
        uint decodedTexelCount = padded.X * padded.Y;
        Buffer decodeBuffer = rendering.CreateBuffer(decodedTexelCount * 2 * (ulong)sizeof(Vector4), MemoryType.GpuOnly);
        RenderContext context = new(rendering, 256);
        try
        {
            byte[] uploadBytes = PackSourceMip(sourceFormat, extent, expected);
            rendering.UploadTexture(source, uploadBytes, new TextureCopyDesc { MipLevel = sourceMip });

            context.BeginFrame();
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                compressor.Record(context, compute, format, source, sourceMip, blockBuffer.Slice(guardBytes, encodedBytes));
            }

            context.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Transfer, Access.TransferRead | Access.TransferWrite);
            context.CopyBufferToTexture(blockBuffer.Slice(guardBytes, encodedBytes), destination);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Compute, Access.ShaderRead);

            uint nativeSlot = destination.SampledIndex;
            uint unormSlot = IsSrgb(format) ? destination.GetSampledIndex(ToUnorm(format)) : nativeSlot;
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                DecodeVerifyRoot decodeRoot = new()
                {
                    Output = decodeBuffer.View,
                    NativeView = nativeSlot,
                    UnormView = unormSlot,
                    Extent = padded,
                };
                compute.Dispatch(decodeShader, in decodeRoot, new Extent3D((padded.X + 7u) / 8u, (padded.Y + 7u) / 8u, 1u));
            }
            context.Submit();
            rendering.WaitIdle();

            byte[] blockReadback = new byte[sentinelInit.Length];
            rendering.ReadBuffer(blockBuffer, blockReadback);
            for (int i = 0; i < (int)guardBytes; ++i)
            {
                Assert.That(blockReadback[i], Is.EqualTo(0xAA), $"{format} {extent.X}x{extent.Y}: prefix sentinel corrupted at {i}");
                Assert.That(blockReadback[(int)guardBytes + (int)encodedBytes + i], Is.EqualTo(0xAA),
                    $"{format} {extent.X}x{extent.Y}: suffix sentinel corrupted at {i}");
            }

            Vector4[] decoded = new Vector4[decodedTexelCount * 2];
            rendering.ReadBuffer(decodeBuffer, MemoryMarshal.AsBytes(decoded.AsSpan()));

            float maxColorErr = 0.0f;
            float maxAlphaErr = 0.0f;
            for (uint y = 0; y < padded.Y; ++y)
            {
                for (uint x = 0; x < padded.X; ++x)
                {
                    uint sx = Math.Min(x, extent.X - 1u);
                    uint sy = Math.Min(y, extent.Y - 1u);
                    Vector4 target = ExpectedTexel(format, expected[sy * extent.X + sx]);
                    int texelIdx = (int)(y * padded.X + x);
                    Vector4 nativeVal = decoded[texelIdx];
                    Vector4 unormVal = decoded[(int)decodedTexelCount + texelIdx];

                    float colorErr = MathF.Max(MathF.Abs(unormVal.X - target.X),
                        MathF.Max(MathF.Abs(unormVal.Y - target.Y), MathF.Abs(unormVal.Z - target.Z)));
                    float alphaErr = MathF.Abs(unormVal.W - target.W);
                    maxColorErr = MathF.Max(maxColorErr, colorErr);
                    maxAlphaErr = MathF.Max(maxAlphaErr, alphaErr);

                    if (IsSrgb(format))
                    {
                        Vector4 expectedNative = Linearize(unormVal);
                        Assert.That(Vector4.DistanceSquared(nativeVal, expectedNative), Is.LessThanOrEqualTo(0.0001f),
                            $"{format} {extent.X}x{extent.Y}: sRGB view mismatch at ({x},{y})");
                    }
                }
            }

            float colorTol = format == Format.Bc4Snorm ? 0.16f : 0.085f;
            float alphaTol = 0.085f;
            Assert.That(maxColorErr, Is.LessThanOrEqualTo(colorTol), $"{format} {extent.X}x{extent.Y} mip {sourceMip}: maxColorErr={maxColorErr:F4}");
            Assert.That(maxAlphaErr, Is.LessThanOrEqualTo(alphaTol), $"{format} {extent.X}x{extent.Y} mip {sourceMip}: maxAlphaErr={maxAlphaErr:F4}");
        }
        finally
        {
            context.DiscardFrame();
            context.Dispose();
            decodeBuffer.Dispose();
            blockBuffer.Dispose();
            destination.Dispose();
            source.Dispose();
        }
    }

    private static void VerifySrgbColorSpaceModes(RenderingSystem rendering, TextureCompressor compressor, Shader decodeShader)
    {
        Extent2D extent = new(16, 16);
        Vector4[] srgbCodes = BuildTestPattern(extent, signed: false);
        byte[] rgba8 = PackSourceMip(Format.Rgba8Unorm, extent, srgbCodes);

        // Upload identical sRGB bytes into a MutableFormat Rgba8Srgb texture and an Rgba16Float linear texture.
        Texture mutableSrgbSrc = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(extent.X, extent.Y, 1),
            Format = Format.Rgba8Srgb,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            MutableFormat = true,
        });
        Texture linearFloatSrc = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(extent.X, extent.Y, 1),
            Format = Format.Rgba16Float,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        Texture nonMutableSrgbSrc = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(extent.X, extent.Y, 1),
            Format = Format.Rgba8Srgb,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            MutableFormat = false,
        });

        Texture? bcFromMutable = null;
        Texture? bcFromFloat = null;
        Texture? bcFromNonMutable = null;
        try
        {
            rendering.UploadTexture(mutableSrgbSrc, rgba8);
            rendering.UploadTexture(nonMutableSrgbSrc, rgba8);

            Half[] fp16 = new Half[srgbCodes.Length * 4];
            for (int i = 0; i < srgbCodes.Length; ++i)
            {
                Vector4 lin = Linearize(srgbCodes[i]);
                fp16[i * 4 + 0] = (Half)lin.X;
                fp16[i * 4 + 1] = (Half)lin.Y;
                fp16[i * 4 + 2] = (Half)lin.Z;
                fp16[i * 4 + 3] = (Half)lin.W;
            }
            rendering.UploadTexture(linearFloatSrc, MemoryMarshal.AsBytes(fp16.AsSpan()));

            bcFromMutable = compressor.CompressToTexture(mutableSrgbSrc, Format.Bc7Srgb);
            bcFromFloat = compressor.CompressToTexture(linearFloatSrc, Format.Bc7Srgb);
            bcFromNonMutable = compressor.CompressToTexture(nonMutableSrgbSrc, Format.Bc7Srgb);

            Vector4[] decodedMutable = HardwareDecodeLinear(rendering, decodeShader, bcFromMutable, extent);
            Vector4[] decodedFloat = HardwareDecodeLinear(rendering, decodeShader, bcFromFloat, extent);
            Vector4[] decodedNonMutable = HardwareDecodeLinear(rendering, decodeShader, bcFromNonMutable, extent);

            for (int i = 0; i < srgbCodes.Length; ++i)
            {
                Vector4 expectedLinear = Linearize(srgbCodes[i]);
                Assert.That(Vector4.Distance(decodedMutable[i], expectedLinear), Is.LessThanOrEqualTo(0.05f),
                    $"Mutable Rgba8Srgb -> Bc7Srgb mismatch at {i}");
                Assert.That(Vector4.Distance(decodedFloat[i], expectedLinear), Is.LessThanOrEqualTo(0.05f),
                    $"Linear Rgba16Float -> Bc7Srgb mismatch at {i}");
                Assert.That(Vector4.Distance(decodedNonMutable[i], expectedLinear), Is.LessThanOrEqualTo(0.05f),
                    $"Non-mutable Rgba8Srgb -> Bc7Srgb mismatch at {i}");
            }
        }
        finally
        {
            bcFromNonMutable?.Dispose();
            bcFromFloat?.Dispose();
            bcFromMutable?.Dispose();
            nonMutableSrgbSrc.Dispose();
            linearFloatSrc.Dispose();
            mutableSrgbSrc.Dispose();
        }
    }

    private static void VerifyMultiMipChainBytes(RenderingSystem rendering, TextureCompressor compressor)
    {
        const uint width = 32;
        const uint height = 32;
        const uint mipLevels = 4;

        Texture source = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1),
            MipLevels = mipLevels,
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        try
        {
            for (uint mip = 0; mip < mipLevels; ++mip)
            {
                Extent2D mipExtent = new(width >> (int)mip, height >> (int)mip);
                Vector4[] pattern = BuildTestPattern(mipExtent, signed: false);
                rendering.UploadTexture(source, PackSourceMip(Format.Rgba8Unorm, mipExtent, pattern),
                    new TextureCopyDesc { MipLevel = mip });
            }

            byte[] chain = compressor.CompressMipChainToBytes(source, Format.Bc7Unorm);
            ulong expectedSize = TextureCompressor.GetTotalCompressedSizeBytes(Format.Bc7Unorm, new Extent2D(width, height), 0, mipLevels);
            Assert.That((ulong)chain.Length, Is.EqualTo(expectedSize));
        }
        finally
        {
            source.Dispose();
        }
    }

    private static void VerifyZeroAllocationRecord(RenderingSystem rendering, TextureCompressor compressor)
    {
        Extent2D extent = new(16, 16);
        Texture source = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(extent.X, extent.Y, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        Buffer output = rendering.CreateBuffer(TextureCompressor.GetCompressedSizeBytes(Format.Bc7Unorm, extent), MemoryType.GpuOnly);
        RenderContext context = new(rendering, 256);
        try
        {
            rendering.UploadTexture(source, new byte[16 * 16 * 4]);
            compressor.WarmUp(Format.Bc7Unorm, convertLinearToSrgb: false);

            // Warm one frame so internal pools and command slots settle.
            context.BeginFrame();
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                compressor.Record(context, compute, Format.Bc7Unorm, source, 0, output.Slice());
            }
            context.Submit();
            rendering.WaitIdle();

            long before = GC.GetAllocatedBytesForCurrentThread();
            context.BeginFrame();
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                compressor.Record(context, compute, Format.Bc7Unorm, source.SampledIndex, 0, extent, output.Slice());
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            context.Submit();
            rendering.WaitIdle();

            Assert.That(after - before, Is.EqualTo(0L), "Steady-state TextureCompressor.Record must allocate zero managed bytes.");
        }
        finally
        {
            context.DiscardFrame();
            context.Dispose();
            output.Dispose();
            source.Dispose();
        }
    }

    private static Vector4[] HardwareDecodeLinear(RenderingSystem rendering, Shader decodeShader, Texture bcTexture, Extent2D extent)
    {
        uint count = extent.X * extent.Y;
        Buffer decodeBuffer = rendering.CreateBuffer(count * 2 * (ulong)sizeof(Vector4), MemoryType.GpuOnly);
        RenderContext context = new(rendering, 256);
        try
        {
            context.BeginFrame();
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                DecodeVerifyRoot root = new()
                {
                    Output = decodeBuffer.View,
                    NativeView = bcTexture.SampledIndex,
                    UnormView = bcTexture.SampledIndex,
                    Extent = extent,
                };
                compute.Dispatch(decodeShader, in root, new Extent3D((extent.X + 7u) / 8u, (extent.Y + 7u) / 8u, 1u));
            }
            context.Submit();
            rendering.WaitIdle();

            Vector4[] raw = new Vector4[count * 2];
            rendering.ReadBuffer(decodeBuffer, MemoryMarshal.AsBytes(raw.AsSpan()));
            return raw.AsSpan(0, (int)count).ToArray();
        }
        finally
        {
            context.DiscardFrame();
            context.Dispose();
            decodeBuffer.Dispose();
        }
    }

    private static Vector4[] BuildTestPattern(Extent2D extent, bool signed)
    {
        Vector4[] pixels = new Vector4[extent.X * extent.Y];
        for (uint y = 0; y < extent.Y; ++y)
        {
            for (uint x = 0; x < extent.X; ++x)
            {
                float fx = extent.X > 1 ? x / (float)(extent.X - 1) : 0.6f;
                float fy = extent.Y > 1 ? y / (float)(extent.Y - 1) : 0.4f;
                float t = extent.X > 1 && extent.Y > 1 ? (fx + 2.0f * fy) / 3.0f : 0.5f * (fx + fy);
                Vector4 v = new(
                    0.15f + 0.7f * t,
                    0.2f + 0.6f * t,
                    0.8f - 0.5f * t,
                    0.95f - 0.75f * fy);
                if (signed)
                    v.X = v.X * 2.0f - 1.0f;
                pixels[y * extent.X + x] = v;
            }
        }
        return pixels;
    }

    private static byte[] PackSourceMip(Format format, Extent2D extent, Vector4[] pixels)
    {
        if (format == Format.Rgba32Float)
        {
            byte[] bytes = new byte[pixels.Length * sizeof(Vector4)];
            MemoryMarshal.AsBytes(pixels.AsSpan()).CopyTo(bytes);
            return bytes;
        }

        byte[] rgba8 = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; ++i)
        {
            rgba8[i * 4 + 0] = (byte)MathF.Round(Math.Clamp(pixels[i].X, 0.0f, 1.0f) * 255.0f);
            rgba8[i * 4 + 1] = (byte)MathF.Round(Math.Clamp(pixels[i].Y, 0.0f, 1.0f) * 255.0f);
            rgba8[i * 4 + 2] = (byte)MathF.Round(Math.Clamp(pixels[i].Z, 0.0f, 1.0f) * 255.0f);
            rgba8[i * 4 + 3] = (byte)MathF.Round(Math.Clamp(pixels[i].W, 0.0f, 1.0f) * 255.0f);
        }
        return rgba8;
    }

    private static Vector4 ExpectedTexel(Format format, Vector4 source)
    {
        if (format is Format.Bc1Unorm or Format.Bc1Srgb)
            return source.W < 0.5f ? Vector4.Zero : new Vector4(source.X, source.Y, source.Z, 1.0f);
        if (format is Format.Bc4Unorm or Format.Bc4Snorm)
            return new Vector4(source.X, 0.0f, 0.0f, 1.0f);
        if (format == Format.Bc5Rg)
            return new Vector4(source.X, source.Y, 0.0f, 1.0f);
        return source;
    }

    private static Vector4 Linearize(Vector4 value)
    {
        for (int channel = 0; channel < 3; ++channel)
        {
            value[channel] = value[channel] <= 0.04045f
                ? value[channel] / 12.92f
                : MathF.Pow((value[channel] + 0.055f) / 1.055f, 2.4f);
        }
        return value;
    }

    private static bool IsSrgb(Format format) => TextureUtility.IsSrgbFormat(format);

    private static Format ToUnorm(Format format) => format switch
    {
        Format.Bc1Srgb => Format.Bc1Unorm,
        Format.Bc2Srgb => Format.Bc2Unorm,
        Format.Bc3Srgb => Format.Bc3Unorm,
        Format.Bc7Srgb => Format.Bc7Unorm,
        _ => format,
    };

    private static bool TryCreateHost(GraphicsBackend backend, [NotNullWhen(true)] out RenderingTestHost? host)
    {
        if (backend == GraphicsBackend.Vulkan && !OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Ignore("Full Vulkan backend is tested on Windows/Linux.");
            host = null;
            return false;
        }
        if (backend == GraphicsBackend.Dx12 && !OperatingSystem.IsWindows())
        {
            Assert.Ignore("D3D12 backend is tested on Windows.");
            host = null;
            return false;
        }
        if (backend == GraphicsBackend.Metal && !OperatingSystem.IsMacOS())
        {
            Assert.Ignore("Metal backend is tested on macOS.");
            host = null;
            return false;
        }

        try
        {
            host = Utility.CreateRenderingSystem(backend);
            return true;
        }
        catch (GPUException ex) when (ex.Error == Error.Unsupported)
        {
            Assert.Ignore($"Backend {backend} is unsupported on this host: {ex.Message}");
            host = null;
            return false;
        }
    }

    private static void RegisterLib(ShaderCompiler compiler, string moduleName)
    {
        string fileName = moduleName + ".slang";
        compiler.LoadModuleFromSource(moduleName, fileName, LoadEmbeddedShader(fileName));
    }

    private static string LoadEmbeddedShader(string logicalName)
    {
        Assembly assembly = typeof(TextureCompressor).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException($"Embedded shader '{logicalName}' not found.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
