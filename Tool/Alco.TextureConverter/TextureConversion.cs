using System.Diagnostics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.TextureConverter;

// ─────────────────────────────────────────────────────────────────────────────
// PNG → block-compressed DDS batch conversion over the engine GPU: decode PNG
// → build the mip chain on the CPU (linear-space 2x2 box filter, alpha stays
// linear) → compress every level with the TextureCompressBc1/TextureCompressBc3
// compute shaders → assemble the file with DdsEncoder. Drives the public
// TextureConverter facade; kept internal with the facade as the only entry.
// ─────────────────────────────────────────────────────────────────────────────
internal static unsafe class TextureConversion
{
	private const string Bc1ModuleName = "TextureCompressBc1";
	private const string Bc3ModuleName = "TextureCompressBc3";

	/// <summary>
	/// Runs the whole batch: collect PNG files under the input path, convert
	/// each to DDS, write them out and aggregate the reports.
	/// </summary>
	/// <param name="options">The conversion options.</param>
	/// <param name="renderingSystem">The engine rendering system (GPU access).</param>
	/// <param name="log">Progress line sink; may be null.</param>
	/// <param name="result">The aggregate outcome, or null on fatal errors.</param>
	/// <param name="error">The fatal error description, or null.</param>
	/// <returns>True when the batch ran to completion.</returns>
	internal static bool TryRun(
		TextureConverterOptions options,
		RenderingSystem renderingSystem,
		Action<string>? log,
		out TextureConverterResult? result,
		out string? error)
	{
		if (!renderingSystem.GraphicsDevice.IsFeatureSupported(GPUFeatures.TextureCompressionBC))
		{
			result = null;
			error = "The graphics device does not support BC texture compression.";
			return false;
		}
		if (!TryCollectInputs(options, out List<string> pngFiles, out error))
		{
			result = null;
			return false;
		}

		DdsDecoder.BcFamily family = ToFamily(options.Format);
		uint blockBytes = DdsDecoder.GetBlockBytes(family);
		string rootDirectory = File.GetAttributes(options.InputPath).HasFlag(FileAttributes.Directory)
			? Path.GetFullPath(options.InputPath)
			: Path.GetDirectoryName(Path.GetFullPath(options.InputPath))!;

		int converted = 0;
		int skipped = 0;
		int failed = 0;
		long totalInputBytes = 0;
		long totalOutputBytes = 0;
		List<TextureConverterFileReport> reports = new(pngFiles.Count);
		Stopwatch totalWatch = Stopwatch.StartNew();

		using IDisposable compressor = CreateCompressor(family, renderingSystem,
			out Func<Texture2D, Span<byte>, int> compress);

		for (int i = 0; i < pngFiles.Count; i++)
		{
			string pngPath = pngFiles[i];
			string displayName = Path.GetRelativePath(rootDirectory, pngPath);
			TextureConverterFileReport report;
			try
			{
				report = ConvertFile(pngPath, displayName, BuildDdsPath(options, rootDirectory, pngPath),
					options, renderingSystem, compress, family, blockBytes);
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

	private static DdsDecoder.BcFamily ToFamily(TextureConverterFormat format)
	{
		return format switch
		{
			TextureConverterFormat.Bc1 => DdsDecoder.BcFamily.BC1,
			_ => DdsDecoder.BcFamily.BC3,
		};
	}

	/// <summary>
	/// Creates the GPU compressor for the family. Both compressors expose the
	/// same <c>CompressBlocks</c> shape without a shared interface, so the
	/// dispatch is captured as a delegate.
	/// </summary>
	private static IDisposable CreateCompressor(
		DdsDecoder.BcFamily family,
		RenderingSystem renderingSystem,
		out Func<Texture2D, Span<byte>, int> compress)
	{
		switch (family)
		{
			case DdsDecoder.BcFamily.BC1:
				TextureCompressorBC1 bc1 = renderingSystem.CreateTextureCompressorBC1(
					renderingSystem.ShaderSystem.GetShader(Bc1ModuleName));
				compress = bc1.CompressBlocks;
				return bc1;
			default:
				TextureCompressorBC3 bc3 = renderingSystem.CreateTextureCompressorBC3(
					renderingSystem.ShaderSystem.GetShader(Bc3ModuleName));
				compress = bc3.CompressBlocks;
				return bc3;
		}
	}

	/// <summary>
	/// Converts one PNG file to a DDS file (or reports why it was skipped).
	/// </summary>
	private static TextureConverterFileReport ConvertFile(
		string pngPath,
		string displayName,
		string ddsPath,
		TextureConverterOptions options,
		RenderingSystem renderingSystem,
		Func<Texture2D, Span<byte>, int> compress,
		DdsDecoder.BcFamily family,
		uint blockBytes)
	{
		Stopwatch watch = Stopwatch.StartNew();
		byte[] pngBytes = File.ReadAllBytes(pngPath);
		long inputBytes = pngBytes.LongLength;

		byte* pixels = ImageDecodeUtility.DecodePng(pngBytes, out int width, out int height);
		byte[] levelZero = new byte[width * height * 4];
		fixed (byte* destination = levelZero)
		{
			Buffer.MemoryCopy(pixels, destination, levelZero.Length, levelZero.Length);
		}
		NativeMemory.Free(pixels);

		if (width % 4 != 0 || height % 4 != 0)
		{
			return new TextureConverterFileReport(TextureConverterFileOutcome.Skipped, displayName, width, height,
				0, inputBytes, 0, 0, 0, watch.Elapsed.TotalMilliseconds,
				$"dimensions {width}x{height} are not multiples of the 4x4 block size — PNG kept");
		}

		List<byte[]> levels = BuildMipChain(levelZero, width, height, options.NoMips);

		byte[] chain = new byte[(int)TotalChainBytes(width, height, levels.Count, blockBytes)];
		int offset = 0;
		for (int level = 0; level < levels.Count; level++)
		{
			byte[] levelPixels = levels[level];
			int levelWidth = Math.Max(1, width >> level);
			int levelHeight = Math.Max(1, height >> level);
			Span<byte> destination = chain.AsSpan(offset, (int)DdsDecoder.GetMipByteCount(width, height, level, blockBytes));
			using Texture2D source = renderingSystem.CreateTexture2D(
				levelPixels,
				(uint)levelWidth,
				(uint)levelHeight,
				ImageLoadOption.Default with { Name = displayName });
			offset += compress(source, destination);
		}

		byte[] dds = DdsEncoder.Encode(width, height, family, chain, levels.Count);

		(double psnr, int maxError) = options.Verify
			? VerifyRoundTrip(dds, levelZero)
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

	/// <summary>
	/// Builds the mip chain starting at level 0, halving in linear space down to
	/// 4x4 (every level must stay a whole number of 4x4 blocks).
	/// </summary>
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
		// Halve while the NEXT level stays a whole number of 4x4 blocks: from a
		// multiple of 4, halving keeps whole blocks exactly when divisible by 8
		// (e.g. 10x10 must stop — its half, 5x5, is not block-aligned).
		while (levelWidth % 8 == 0 && levelHeight % 8 == 0)
		{
			source = DownsampleLinearSpace(source, levelWidth, levelHeight);
			levelWidth /= 2;
			levelHeight /= 2;
			levels.Add(source);
		}
		return levels;
	}

	/// <summary>
	/// Halves an RGBA8 image with a 2x2 box filter applied in linear space:
	/// RGB channels round-trip through the sRGB transfer function, alpha (a
	/// linear quantity) is averaged directly.
	/// </summary>
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

	/// <summary>sRGB byte → linear float lookup (built once, read-only).</summary>
	private static readonly float[] SrgbToLinear = BuildSrgbToLinearTable();

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

	/// <summary>
	/// Decodes level 0 of the encoded DDS back to RGBA8 and compares it with the
	/// source pixels: peak signal-to-noise ratio and maximum absolute byte error.
	/// </summary>
	private static (double PsnrDecibel, int MaxError) VerifyRoundTrip(byte[] dds, byte[] sourcePixels)
	{
		DdsDecoder.ParseHeader(dds, srgb: false, out DdsDecoder.BcFamily family, out _,
			out int width, out int height, out _, out int dataOffset);
		byte* decoded = BcDecoder.DecodeLevel(dds, dataOffset, family, width, height, 0);
		int length = width * height * 4;

		double squaredError = 0;
		int maxError = 0;
		for (int i = 0; i < length; i++)
		{
			int delta = decoded[i] - sourcePixels[i];
			int absolute = delta < 0 ? -delta : delta;
			if (absolute > maxError)
			{
				maxError = absolute;
			}
			squaredError += (double)delta * delta;
		}
		NativeMemory.Free(decoded);

		double meanSquaredError = squaredError / length;
		double psnr = meanSquaredError == 0
			? double.PositiveInfinity
			: 10.0 * Math.Log10(255.0 * 255.0 / meanSquaredError);
		return (psnr, maxError);
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

	/// <summary>
	/// Collects the PNG files to convert: one explicit file, or every PNG under
	/// the input directory (recursive, sorted for stable logs).
	/// </summary>
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

	/// <summary>
	/// Resolves the output DDS path: next to the source (InPlace or default),
	/// or mirroring the source's relative path under the output directory.
	/// </summary>
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
