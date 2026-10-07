namespace Alco.TextureConverter;

/// <summary>
/// The per-file outcome of one conversion attempt.
/// </summary>
public enum TextureConverterFileOutcome
{
	/// <summary>The DDS file was written.</summary>
	Converted,

	/// <summary>The file was left as PNG (e.g. dimensions not block-aligned).</summary>
	Skipped,

	/// <summary>The conversion threw; see <see cref="TextureConverterFileReport.Note"/>.</summary>
	Failed,
}

/// <summary>
/// The per-file report of one conversion attempt.
/// </summary>
/// <param name="Outcome">What happened to the file.</param>
/// <param name="DisplayName">The path shown in logs (relative to the input root).</param>
/// <param name="Width">Source width in pixels (0 when the file failed before decode).</param>
/// <param name="Height">Source height in pixels (0 when the file failed before decode).</param>
/// <param name="Levels">The number of encoded mip levels.</param>
/// <param name="InputBytes">The source PNG file size.</param>
/// <param name="OutputBytes">The written DDS file size.</param>
/// <param name="PsnrDecibel">Level-0 round-trip PSNR in dB (<see cref="double.PositiveInfinity"/> for an exact round trip); 0 unless Verify was requested.</param>
/// <param name="MaxError">Level-0 round-trip maximum absolute byte error; 0 unless Verify was requested.</param>
/// <param name="Milliseconds">The wall-clock duration of the conversion.</param>
/// <param name="Note">The skip reason or failure message, or null on success.</param>
public sealed record TextureConverterFileReport(
	TextureConverterFileOutcome Outcome,
	string DisplayName,
	int Width,
	int Height,
	int Levels,
	long InputBytes,
	long OutputBytes,
	double PsnrDecibel,
	int MaxError,
	double Milliseconds,
	string? Note);

/// <summary>
/// The aggregate result of one batch conversion run.
/// </summary>
/// <param name="Converted">The number of files written as DDS.</param>
/// <param name="Skipped">The number of files left as PNG.</param>
/// <param name="Failed">The number of files whose conversion threw.</param>
/// <param name="InputBytes">Total source bytes of the converted files.</param>
/// <param name="OutputBytes">Total DDS bytes of the converted files.</param>
/// <param name="Milliseconds">The wall-clock duration of the whole batch.</param>
/// <param name="Files">The per-file reports, in processing order.</param>
public sealed record TextureConverterResult(
	int Converted,
	int Skipped,
	int Failed,
	long InputBytes,
	long OutputBytes,
	double Milliseconds,
	IReadOnlyList<TextureConverterFileReport> Files);
