namespace Alco.TextureConverter;

/// <summary>
/// The block-compression format the converter produces.
/// </summary>
public enum TextureConverterFormat
{
	/// <summary>BC1 (DXT1): opaque RGB at 4 bits per pixel.</summary>
	Bc1,

	/// <summary>BC3 (DXT5): RGB plus an interpolated alpha channel.</summary>
	Bc3,
}

/// <summary>
/// The options of one batch conversion run.
/// </summary>
/// <param name="InputPath">A PNG file, or a directory converted recursively.</param>
/// <param name="Format">The block-compression format to encode.</param>
/// <param name="InPlace">Replace the source PNG files with DDS files; exclusive with <paramref name="OutputPath"/>.</param>
/// <param name="NoMips">Encode level 0 only, without a generated mip chain.</param>
/// <param name="OutputPath">Mirror the input's relative directory tree under this directory (default: write DDS files next to their sources).</param>
/// <param name="Verify">Decode level 0 back and report PSNR / maximum error against the source.</param>
public sealed record TextureConverterOptions(
	string InputPath,
	TextureConverterFormat Format,
	bool InPlace = false,
	bool NoMips = false,
	string? OutputPath = null,
	bool Verify = false)
{
	/// <summary>
	/// Validates the option combination (paths, mutual exclusion); GPU support
	/// is checked later against the actual device.
	/// </summary>
	/// <param name="error">The validation failure, or null when valid.</param>
	/// <returns>True when the combination is usable.</returns>
	public bool Validate(out string? error)
	{
		if (string.IsNullOrWhiteSpace(InputPath))
		{
			error = "The input path is empty.";
			return false;
		}
		if (InPlace && OutputPath != null)
		{
			error = "InPlace and OutputPath are mutually exclusive.";
			return false;
		}
		error = null;
		return true;
	}
}
