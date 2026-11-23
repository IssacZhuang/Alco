using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;
using Buffer = Alco.Graphics.Buffer;

namespace Alco.TextureConverter;

internal static unsafe class TextureConversion
{
    private const string DecodeVerifySource = """
        #language slang 2025
        module TextureConverterDecodeVerify;

        import Core;

        [GPURoot]
        public struct VerifyRoot
        {
            public GPUBufferView Output;
            public GpuTextureId Source;
            public uint2 Extent;
        };

        [shader("compute")]
        [numthreads(8, 8, 1)]
        void mainCS(uint3 thread : SV_DispatchThreadID)
        {
            VerifyRoot root = gpu_load_root<VerifyRoot>();
            if (any(thread.xy >= root.Extent))
            {
                return;
            }
            Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Source.index);
            uint pixel = thread.y * root.Extent.x + thread.x;
            gpu_store<float4>(root.Output, pixel, source.Load(int3(thread.xy, 0)));
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct VerifyRoot
    {
        public GPUBufferView Output;
        public uint Source;
        public Extent2D Extent;
    }

    private static readonly float[] SrgbToLinear = BuildSrgbToLinearTable();

    private static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(), new JsonConverterPadding() },
    };

    internal static bool TryRun(
        TextureConverterOptions options,
        RenderingSystem renderingSystem,
        Action<string>? log,
        out TextureConverterResult? result,
        out string? error)
    {
        Format targetFormat = ResolveGraphicsFormat(options);
        if (!TextureCompressor.CanCompressOnGpu(renderingSystem.Device, targetFormat))
        {
            result = null;
            error = $"The graphics device does not support GPU BC texture compression for format {targetFormat}.";
            return false;
        }
        if (!TryCollectInputs(options, out List<string> pngFiles, out error))
        {
            result = null;
            return false;
        }

        TextureFormatInfo formatInfo = Gpu.GetTextureFormatInfo(targetFormat);
        uint blockBytes = formatInfo.BytesPerBlock;
        string rootDirectory = File.GetAttributes(options.InputPath).HasFlag(FileAttributes.Directory)
            ? Path.GetFullPath(options.InputPath)
            : Path.GetDirectoryName(Path.GetFullPath(options.InputPath))!;

        TextureCompressor compressor = new(renderingSystem);
        compressor.WarmUp(targetFormat, convertLinearToSrgb: false);
        Shader? verifyShader = options.Verify
            ? renderingSystem.Shaders.GetShaderFromSource(
                "TextureConverterDecodeVerify",
                "TextureConverterDecodeVerify.slang",
                DecodeVerifySource,
                ShaderKind.Compute,
                "TextureConverterDecodeVerify")
            : null;

        Dictionary<string, bool?> metaCache = new(StringComparer.OrdinalIgnoreCase);
        int converted = 0;
        int skipped = 0;
        int failed = 0;
        long totalInputBytes = 0;
        long totalOutputBytes = 0;
        List<TextureConverterFileReport> reports = new(pngFiles.Count);
        Stopwatch totalWatch = Stopwatch.StartNew();

        Buffer? scratchBuffer = null;
        RenderContext? context = new(renderingSystem, 256);
        try
        {
            for (int i = 0; i < pngFiles.Count; i++)
            {
                string pngPath = pngFiles[i];
                string displayName = Path.GetRelativePath(rootDirectory, pngPath);
                TextureConverterFileReport report;
                try
                {
                    bool premultiplyAlpha = ResolvePremultiplyAlpha(pngPath, options, metaCache);
                    report = ConvertFile(
                        pngPath,
                        displayName,
                        BuildDdsPath(options, rootDirectory, pngPath),
                        options,
                        renderingSystem,
                        compressor,
                        context,
                        ref scratchBuffer,
                        verifyShader,
                        targetFormat,
                        blockBytes,
                        premultiplyAlpha);
                }
                catch (Exception exception)
                {
                    report = new TextureConverterFileReport(TextureConverterFileOutcome.Failed, displayName,
                        0, 0, 0, 0, 0, 0, 0, 0, exception.Message);
                }

                reports.Add(report);
                LogReport(log, i + 1, pngFiles.Count, report);

                switch (report.Outcome)
                {
                    case TextureConverterFileOutcome.Converted:
                        converted++;
                        totalInputBytes += report.InputBytes;
                        totalOutputBytes += report.OutputBytes;
                        break;
                    case TextureConverterFileOutcome.Skipped:
                        skipped++;
                        break;
                    default:
                        failed++;
                        break;
                }
            }
        }
        finally
        {
            context.DiscardFrame();
            context.Destroy();
            scratchBuffer?.Destroy();
        }

        totalWatch.Stop();
        log?.Invoke(
            $"{converted} converted, {skipped} skipped, {failed} failed in {totalWatch.Elapsed.TotalMilliseconds:F0}ms: " +
            $"{totalInputBytes / 1024.0:F0}KB → {totalOutputBytes / 1024.0:F0}KB " +
            $"({(totalInputBytes == 0 ? 0 : 100.0 * totalOutputBytes / totalInputBytes):F0}% of source).");

        result = new TextureConverterResult(converted, skipped, failed, totalInputBytes, totalOutputBytes,
            totalWatch.Elapsed.TotalMilliseconds, reports);
        error = null;
        return true;
    }

    private static TextureConverterFileReport ConvertFile(
        string pngPath,
        string displayName,
        string ddsPath,
        TextureConverterOptions options,
        RenderingSystem renderingSystem,
        TextureCompressor compressor,
        RenderContext context,
        ref Buffer? scratchBuffer,
        Shader? verifyShader,
        Format targetFormat,
        uint blockBytes,
        bool premultiplyAlpha)
    {
        Stopwatch watch = Stopwatch.StartNew();
        byte[] pngBytes = File.ReadAllBytes(pngPath);
        long inputBytes = pngBytes.LongLength;

        byte* pixels = ImageDecodeUtility.DecodePng(pngBytes, out int width, out int height);
        byte[] levelZero = new byte[checked(width * height * 4)];
        try
        {
            fixed (byte* destination = levelZero)
            {
                System.Buffer.MemoryCopy(pixels, destination, levelZero.Length, levelZero.Length);
            }
        }
        finally
        {
            NativeMemory.Free(pixels);
        }

        if (width % 4 != 0 || height % 4 != 0)
        {
            return new TextureConverterFileReport(TextureConverterFileOutcome.Skipped, displayName, width, height,
                0, inputBytes, 0, 0, 0, watch.Elapsed.TotalMilliseconds,
                $"dimensions {width}x{height} are not multiples of the 4x4 block size — PNG kept");
        }

        if (premultiplyAlpha)
        {
            ImageUtility.PremultiplyRgba8(levelZero);
        }

        List<byte[]> levels = BuildMipChain(levelZero, width, height, options.NoMips);
        ulong totalChainBytes = (ulong)TotalChainBytes(width, height, levels.Count, blockBytes);
        if (scratchBuffer == null || scratchBuffer.SizeBytes < totalChainBytes)
        {
            scratchBuffer?.Destroy();
            scratchBuffer = renderingSystem.CreateBuffer(Math.Max(totalChainBytes, 256UL * 1024), MemoryType.GpuOnly);
        }

        Texture sourceTexture = renderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = (uint)levels.Count,
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });

        byte[] chain = new byte[checked((int)totalChainBytes)];
        try
        {
            for (int level = 0; level < levels.Count; ++level)
            {
                renderingSystem.UploadTexture(sourceTexture, levels[level], new TextureCopyDesc { MipLevel = (uint)level });
            }

            context.BeginFrame();
            ulong byteOffset = 0;
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                for (int level = 0; level < levels.Count; ++level)
                {
                    uint mipWidth = (uint)Math.Max(1, width >> level);
                    uint mipHeight = (uint)Math.Max(1, height >> level);
                    ulong mipBytes = DdsDecoder.GetMipByteCount(width, height, level, blockBytes);
                    compressor.Record(
                        context,
                        compute,
                        targetFormat,
                        sourceTexture.SampledIndex,
                        (uint)level,
                        new Extent2D(mipWidth, mipHeight),
                        scratchBuffer.Range.Slice(byteOffset, mipBytes),
                        convertLinearToSrgb: false);
                    byteOffset += mipBytes;
                }
            }
            renderingSystem.Submit([context]).Wait();
            renderingSystem.ReadBuffer(scratchBuffer, chain);
        }
        finally
        {
            sourceTexture.Destroy();
        }

        byte[] dds = DdsEncoder.Encode(width, height, targetFormat, chain, levels.Count);

        (double psnr, int maxError) = verifyShader != null
            ? VerifyRoundTripGpu(renderingSystem, context, verifyShader, targetFormat, chain, levelZero, width, height)
            : (0, 0);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ddsPath))!);
        File.WriteAllBytes(ddsPath, dds);
        if (options.InPlace)
        {
            File.Delete(pngPath);
        }

        watch.Stop();
        return new TextureConverterFileReport(TextureConverterFileOutcome.Converted, displayName, width, height,
            levels.Count, inputBytes, dds.LongLength, psnr, maxError, watch.Elapsed.TotalMilliseconds, null);
    }

    private static (double PsnrDecibel, int MaxError) VerifyRoundTripGpu(
        RenderingSystem renderingSystem,
        RenderContext context,
        Shader verifyShader,
        Format encodedFormat,
        ReadOnlySpan<byte> compressedChain,
        byte[] sourcePixels,
        int width,
        int height)
    {
        Format verifyFormat = ToUnormFormat(encodedFormat);
        uint blockBytes = Gpu.GetTextureFormatInfo(verifyFormat).BytesPerBlock;
        int level0Bytes = (int)DdsDecoder.GetMipByteCount(width, height, 0, blockBytes);
        int texelCount = width * height;

        Texture bcTexture = renderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D((uint)width, (uint)height, 1),
            Format = verifyFormat,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        Buffer decodeBuffer = renderingSystem.CreateBuffer((ulong)texelCount * (ulong)sizeof(Vector4), MemoryType.GpuOnly);
        Vector4[] decoded = new Vector4[texelCount];
        try
        {
            renderingSystem.UploadTexture(bcTexture, compressedChain[..level0Bytes]);
            context.BeginFrame();
            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                GPURange root = context.AllocateRoot(new VerifyRoot
                {
                    Output = decodeBuffer.View,
                    Source = bcTexture.SampledIndex,
                    Extent = new Extent2D((uint)width, (uint)height),
                });
                compute.Dispatch(verifyShader, root, new Extent3D(((uint)width + 7u) / 8u, ((uint)height + 7u) / 8u, 1u));
            }
            renderingSystem.Submit([context]).Wait();
            renderingSystem.ReadBuffer(decodeBuffer, MemoryMarshal.AsBytes(decoded.AsSpan()));
        }
        finally
        {
            decodeBuffer.Destroy();
            bcTexture.Destroy();
        }

        int channelMask = encodedFormat switch
        {
            Format.Bc1Unorm or Format.Bc1Srgb => 7, // Compare RGB cleanly; alpha is 1-bit punch-through.
            Format.Bc4Unorm or Format.Bc4Snorm => 1,
            Format.Bc5Rg => 3,
            _ => 15,
        };

        double squaredError = 0;
        int samples = 0;
        int maxError = 0;
        bool isSigned = encodedFormat == Format.Bc4Snorm;
        for (int i = 0; i < texelCount; ++i)
        {
            Vector4 actual = decoded[i];
            for (int c = 0; c < 4; ++c)
            {
                if ((channelMask & (1 << c)) == 0)
                    continue;
                int actualByte = isSigned
                    ? Math.Clamp((int)MathF.Round((actual[c] * 0.5f + 0.5f) * 255.0f), 0, 255)
                    : Math.Clamp((int)MathF.Round(actual[c] * 255.0f), 0, 255);
                int delta = actualByte - sourcePixels[i * 4 + c];
                int abs = delta < 0 ? -delta : delta;
                if (abs > maxError)
                    maxError = abs;
                squaredError += (double)delta * delta;
                samples++;
            }
        }

        double mse = samples == 0 ? 0 : squaredError / samples;
        double psnr = mse == 0 ? double.PositiveInfinity : 10.0 * Math.Log10(255.0 * 255.0 / mse);
        return (psnr, maxError);
    }

    private static bool ResolvePremultiplyAlpha(string pngPath, TextureConverterOptions options, Dictionary<string, bool?> metaCache)
    {
        if (options.PremultiplyAlpha is bool forced)
        {
            return forced;
        }

        List<string> directories = [];
        string? dir = Path.GetDirectoryName(Path.GetFullPath(pngPath));
        while (!string.IsNullOrEmpty(dir))
        {
            directories.Add(dir);
            dir = Path.GetDirectoryName(dir);
        }

        bool premultiply = false;
        for (int i = directories.Count - 1; i >= 0; --i)
        {
            string metaPath = Path.Combine(directories[i], ".texture-option.meta");
            if (TryReadPremultiplyFlag(metaPath, metaCache) is bool flag)
            {
                premultiply = flag;
            }
        }

        string fileMetaPath = Path.GetFullPath(pngPath) + ".meta";
        if (TryReadPremultiplyFlag(fileMetaPath, metaCache) is bool fileFlag)
        {
            premultiply = fileFlag;
        }

        return premultiply;
    }

    private static bool? TryReadPremultiplyFlag(string metaPath, Dictionary<string, bool?> cache)
    {
        if (cache.TryGetValue(metaPath, out bool? cached))
        {
            return cached;
        }

        bool? result = null;
        if (File.Exists(metaPath))
        {
            string json = File.ReadAllText(metaPath);
            ImageMeta? meta = JsonSerializer.Deserialize<ImageMeta>(json, MetaJsonOptions);
            result = meta?.PremultiplyAlpha;
        }
        cache[metaPath] = result;
        return result;
    }

    private static Format ResolveGraphicsFormat(TextureConverterOptions options) => (options.Format, options.Srgb) switch
    {
        (TextureConverterFormat.Bc1, false) => Format.Bc1Unorm,
        (TextureConverterFormat.Bc1, true) => Format.Bc1Srgb,
        (TextureConverterFormat.Bc2, false) => Format.Bc2Unorm,
        (TextureConverterFormat.Bc2, true) => Format.Bc2Srgb,
        (TextureConverterFormat.Bc3, false) => Format.Bc3Unorm,
        (TextureConverterFormat.Bc3, true) => Format.Bc3Srgb,
        (TextureConverterFormat.Bc4, _) => Format.Bc4Unorm,
        (TextureConverterFormat.Bc4Snorm, _) => Format.Bc4Snorm,
        (TextureConverterFormat.Bc5, _) => Format.Bc5Rg,
        (TextureConverterFormat.Bc7, false) => Format.Bc7Unorm,
        (TextureConverterFormat.Bc7, true) => Format.Bc7Srgb,
        _ => throw new ArgumentOutOfRangeException(nameof(options), options.Format, null),
    };

    private static Format ToUnormFormat(Format format) => format switch
    {
        Format.Bc1Srgb => Format.Bc1Unorm,
        Format.Bc2Srgb => Format.Bc2Unorm,
        Format.Bc3Srgb => Format.Bc3Unorm,
        Format.Bc7Srgb => Format.Bc7Unorm,
        _ => format,
    };

    private static List<byte[]> BuildMipChain(byte[] levelZero, int width, int height, bool noMips)
    {
        List<byte[]> levels = [levelZero];
        if (noMips)
        {
            return levels;
        }

        byte[] source = levelZero;
        int levelWidth = width;
        int levelHeight = height;
        while (levelWidth % 8 == 0 && levelHeight % 8 == 0)
        {
            source = DownsampleLinearSpace(source, levelWidth, levelHeight);
            levelWidth /= 2;
            levelHeight /= 2;
            levels.Add(source);
        }
        return levels;
    }

    private static byte[] DownsampleLinearSpace(byte[] source, int width, int height)
    {
        int newWidth = width / 2;
        int newHeight = height / 2;
        byte[] destination = new byte[newWidth * newHeight * 4];
        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                float red = 0;
                float green = 0;
                float blue = 0;
                float alpha = 0;
                for (int dy = 0; dy < 2; dy++)
                {
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int index = ((y * 2 + dy) * width + x * 2 + dx) * 4;
                        red += SrgbToLinear[source[index]];
                        green += SrgbToLinear[source[index + 1]];
                        blue += SrgbToLinear[source[index + 2]];
                        alpha += source[index + 3];
                    }
                }

                int destinationOffset = (y * newWidth + x) * 4;
                destination[destinationOffset] = LinearToSrgbByte(red * 0.25f);
                destination[destinationOffset + 1] = LinearToSrgbByte(green * 0.25f);
                destination[destinationOffset + 2] = LinearToSrgbByte(blue * 0.25f);
                destination[destinationOffset + 3] = (byte)MathF.Round(alpha * 0.25f);
            }
        }
        return destination;
    }

    private static float[] BuildSrgbToLinearTable()
    {
        float[] table = new float[256];
        for (int i = 0; i < 256; i++)
        {
            float value = i / 255f;
            table[i] = value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
        }
        return table;
    }

    private static byte LinearToSrgbByte(float linear)
    {
        if (linear <= 0.0031308f)
        {
            return (byte)Math.Clamp(MathF.Round(linear * 12.92f * 255f), 0, 255);
        }
        float srgb = 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
        return (byte)Math.Clamp(MathF.Round(srgb * 255f), 0, 255);
    }

    private static long TotalChainBytes(int width, int height, int levelCount, uint blockBytes)
    {
        long total = 0;
        for (int level = 0; level < levelCount; level++)
        {
            total += DdsDecoder.GetMipByteCount(width, height, level, blockBytes);
        }
        return total;
    }

    private static bool TryCollectInputs(TextureConverterOptions options, out List<string> pngFiles, out string? error)
    {
        if (File.Exists(options.InputPath))
        {
            if (!string.Equals(Path.GetExtension(options.InputPath), ".png", StringComparison.OrdinalIgnoreCase))
            {
                pngFiles = [];
                error = $"The input file '{options.InputPath}' is not a PNG.";
                return false;
            }
            pngFiles = [Path.GetFullPath(options.InputPath)];
            error = null;
            return true;
        }

        pngFiles = [.. Directory
            .EnumerateFiles(options.InputPath, "*.png", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetFullPath(path))];
        error = pngFiles.Count == 0 ? $"No PNG files found under '{options.InputPath}'." : null;
        return pngFiles.Count > 0;
    }

    private static string BuildDdsPath(TextureConverterOptions options, string rootDirectory, string pngPath)
    {
        if (options.OutputPath == null)
        {
            return Path.ChangeExtension(pngPath, ".dds");
        }
        string relative = Path.GetRelativePath(rootDirectory, pngPath);
        return Path.Combine(options.OutputPath, Path.ChangeExtension(relative, ".dds"));
    }

    private static void LogReport(Action<string>? log, int index, int count, TextureConverterFileReport report)
    {
        if (log == null)
        {
            return;
        }
        string prefix = $"[{index}/{count}] {report.DisplayName} {report.Width}x{report.Height}";
        switch (report.Outcome)
        {
            case TextureConverterFileOutcome.Converted:
                string verify = report.PsnrDecibel > 0
                    ? $", PSNR {(report.PsnrDecibel == double.PositiveInfinity ? "inf" : report.PsnrDecibel.ToString("F1"))}dB, maxErr {report.MaxError}"
                    : "";
                log($"{prefix} → {report.Levels} level(s), {report.OutputBytes / 1024.0:F0}KB (was {report.InputBytes / 1024.0:F0}KB), {report.Milliseconds:F0}ms{verify}");
                break;
            case TextureConverterFileOutcome.Skipped:
                log($"{prefix} skipped: {report.Note}");
                break;
            default:
                log($"{prefix} FAILED: {report.Note}");
                break;
        }
    }
}
