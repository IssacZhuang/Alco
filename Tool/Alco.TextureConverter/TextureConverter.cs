namespace Alco.TextureConverter;

/// <summary>
/// Batch-converts PNG textures to block-compressed DDS files on the GPU
/// (TextureCompressBc1/TextureCompressBc3 compute shaders), for the asset
/// packaging pipeline. One instance owns one headless engine; instances are
/// not thread-safe.
/// </summary>
/// <remarks>
/// Encoded variants are LINEAR BC (raw byte passthrough): the runtime uploads
/// DDS payloads as BC?RGBA8Unorm, matching the sampling semantics of the
/// PNG → RGBA8Unorm path. Scope note: Item/Apparel/Weapon* texture groups load
/// with PremultiplyAlpha; extending the converter to them requires
/// premultiplying on the encode side first.
/// </remarks>
public sealed class TextureConverter : IDisposable
{
	private ConverterEngine? _engine;

	/// <summary>
	/// Receives one progress line per converted file plus a summary line;
	/// null (default) is silent. Skips and failures are routed here too,
	/// prefixed in the line text.
	/// </summary>
	public Action<string>? Log { get; init; }

	/// <summary>
	/// Converts one PNG file, or every PNG under one directory, to DDS.
	/// </summary>
	/// <param name="options">What to convert and how.</param>
	/// <param name="result">The per-file and aggregate outcome, or null on fatal errors.</param>
	/// <param name="error">The fatal error description (missing input, unsupported device), or null.</param>
	/// <returns>True when the batch ran; individual file failures are reported through <paramref name="result"/>, not here.</returns>
	public bool TryConvert(
		TextureConverterOptions options,
		out TextureConverterResult? result,
		out string? error)
	{
		if (!options.Validate(out error))
		{
			result = null;
			return false;
		}
		if (!File.Exists(options.InputPath) && !Directory.Exists(options.InputPath))
		{
			result = null;
			error = $"The input path '{options.InputPath}' does not exist.";
			return false;
		}

		_engine ??= new ConverterEngine();
		return TextureConversion.TryRun(options, _engine.RenderingSystem, Log, out result, out error);
	}

	/// <summary>
	/// Releases the headless engine (the GPU device and its resources).
	/// </summary>
	public void Dispose()
	{
		_engine?.Dispose();
		_engine = null;
	}
}
