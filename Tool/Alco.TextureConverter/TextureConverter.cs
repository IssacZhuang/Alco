namespace Alco.TextureConverter;

/// <summary>
/// Batch-converts PNG textures to BC1-BC7 compressed DDS files on the GPU using bindless compute shaders.
/// One instance owns one headless engine; instances are not thread-safe.
/// </summary>
public sealed class TextureConverter : IDisposable
{
    private ConverterEngine? _engine;

    /// <summary>Optional per-file progress and summary log sink.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>Converts one PNG file, or every PNG recursively under a directory, to DDS.</summary>
    /// <param name="options">Conversion settings and target BC format.</param>
    /// <param name="result">Per-file and aggregate report, or null on fatal startup errors.</param>
    /// <param name="error">Fatal validation or device-capability error description, or null.</param>
    /// <returns><c>true</c> when batch execution finished; per-file skips or errors appear inside <paramref name="result"/>.</returns>
    public bool TryConvert(
        TextureConverterOptions options,
        out TextureConverterResult? result,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);
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

    /// <summary>Releases the owned headless engine and GPU device.</summary>
    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
    }
}
