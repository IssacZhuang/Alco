using System.Text.Json;

namespace Alco.Rendering;

/// <summary>
/// Shared JSON plumbing of the authorable asset formats (<c>.amat</c>/<c>.amdl</c>).
/// Version handling follows the mesh asset format convention: a version whose major digit
/// exceeds the supported major is rejected, minor differences are forward compatible, and
/// unknown fields are ignored.
/// </summary>
public static class AssetJson
{
    /// <summary>
    /// Author-friendly serializer options: camelCase names (case-insensitive reads),
    /// tolerant of comments and trailing commas.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Validate the version field of a parsed asset file against the supported version.
    /// </summary>
    /// <param name="version">The version string read from the file; null when the file has none.</param>
    /// <param name="supportedVersion">The supported version (major must not be exceeded).</param>
    /// <param name="formatName">The format name for error messages.</param>
    /// <param name="filename">The file being parsed, for error context.</param>
    /// <exception cref="InvalidDataException">Thrown when the version is missing, malformed
    /// or has an unsupported major digit.</exception>
    public static void ValidateVersion(string? version, string supportedVersion, string formatName, string filename)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidDataException($"{formatName} '{filename}' has no version; expected '{supportedVersion}'.");
        }

        ReadOnlySpan<char> versionSpan = version.AsSpan();
        int dot = versionSpan.IndexOf('.');
        ReadOnlySpan<char> majorText = dot >= 0 ? versionSpan[..dot] : versionSpan;
        if (!int.TryParse(majorText, out int major) || major < 0)
        {
            throw new InvalidDataException($"{formatName} '{filename}' has malformed version '{version}'; expected '{supportedVersion}'.");
        }

        int supportedMajor = int.TryParse(supportedVersion.AsSpan(..supportedVersion.IndexOf('.')), out int parsed) ? parsed : 0;
        if (major > supportedMajor)
        {
            throw new InvalidDataException($"{formatName} '{filename}' uses version '{version}' which is not supported (supported major: {supportedMajor}).");
        }
    }

    /// <summary>
    /// Normalize an authored texture/material reference path: backslashes become asset-root
    /// separators ('/'), surrounding whitespace is trimmed and empty references become null.
    /// </summary>
    /// <param name="path">The authored path.</param>
    /// <returns>The normalized path, or null when the reference is empty.</returns>
    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim();
        return trimmed.Length == 0 ? null : trimmed.Replace('\\', '/');
    }
}
