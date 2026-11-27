using System;
using System.IO;
using System.Text;

namespace Alco.IO;

internal static class RelativePathUtility
{
    public static string NormalizeRelativeFilePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Relative path must not be empty.", nameof(relativePath));

        string normalized = ValidateAndNormalizeSegments(relativePath, allowEmptyDirectory: false);
        if (relativePath.EndsWith('/') || relativePath.EndsWith('\\'))
            throw new ArgumentException($"File path '{relativePath}' must not end with a directory separator.", nameof(relativePath));

        return normalized;
    }

    public static string NormalizeRelativeDirectoryPath(string relativeDirectory)
    {
        ArgumentNullException.ThrowIfNull(relativeDirectory);

        string trimmed = relativeDirectory.Trim();
        if (trimmed.Length == 0 || trimmed == ".")
            return string.Empty;

        return ValidateAndNormalizeSegments(trimmed, allowEmptyDirectory: true);
    }

    private static string ValidateAndNormalizeSegments(string path, bool allowEmptyDirectory)
    {
        if (Path.IsPathRooted(path) || path.Contains(':'))
            throw new ArgumentException($"Path '{path}' must be relative to the storage root.", nameof(path));

        string replaced = path.Replace('\\', '/').TrimStart('/');
        if (allowEmptyDirectory)
            replaced = replaced.TrimEnd('/');

        if (replaced.Length == 0)
        {
            if (allowEmptyDirectory)
                return string.Empty;

            throw new ArgumentException($"Invalid empty path '{path}'.", nameof(path));
        }

        var builder = new StringBuilder(replaced.Length);
        int start = 0;
        while (start <= replaced.Length)
        {
            int nextSlash = replaced.IndexOf('/', start);
            int length = nextSlash >= 0 ? nextSlash - start : replaced.Length - start;
            ReadOnlySpan<char> segment = replaced.AsSpan(start, length);

            if (segment.IsEmpty || segment is "." or "..")
                throw new ArgumentException($"Invalid path segment in '{path}'.", nameof(path));

            if (builder.Length > 0)
                builder.Append('/');

            builder.Append(segment);
            if (nextSlash < 0)
                break;

            start = nextSlash + 1;
        }

        return builder.ToString();
    }
}
