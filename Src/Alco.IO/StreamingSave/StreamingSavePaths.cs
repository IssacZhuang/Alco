using System;
using System.Globalization;
using System.IO;

namespace Alco.IO;

internal static class StreamingSavePaths
{
    public const string WorldsDirectory = "worlds";
    public const string SegmentFilePattern = "seg_*.chk";
    public const string SnapshotFilePattern = "*.snap";
    public const string SpillFilePattern = "spill_*.tmp";

    private static readonly char[] InvalidSlotChars = ComputeInvalidSlotChars();

    public static string WorldDirectory(Guid worldGuid) => $"{WorldsDirectory}/{worldGuid:N}";

    public static string ChunksDirectory(Guid worldGuid) => $"{WorldsDirectory}/{worldGuid:N}/chunks";

    public static string SegmentFilePath(Guid worldGuid, uint segmentId)
    {
        if (segmentId == 0)
            throw new ArgumentOutOfRangeException(nameof(segmentId), "Committed segment ID must be >= 1.");

        return $"{WorldsDirectory}/{worldGuid:N}/chunks/seg_{segmentId:D6}.chk";
    }

    public static string SnapshotsDirectory(Guid worldGuid) => $"{WorldsDirectory}/{worldGuid:N}/snapshots";

    public static string SnapshotFilePath(Guid worldGuid, string slotId)
    {
        ValidateSlotId(slotId);
        return $"{WorldsDirectory}/{worldGuid:N}/snapshots/{slotId}.snap";
    }

    public static string SessionDirectory(Guid worldGuid) => $"{WorldsDirectory}/{worldGuid:N}/session";

    public static string SpillFilePath(Guid worldGuid, Guid sessionGuid) =>
        $"{WorldsDirectory}/{worldGuid:N}/session/spill_{sessionGuid:N}.tmp";

    public static void ValidateSlotId(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
            throw new ArgumentException("Slot ID must not be empty or whitespace.", nameof(slotId));

        if (slotId is "." or ".." ||
            slotId.Contains("..", StringComparison.Ordinal) ||
            slotId.IndexOfAny(InvalidSlotChars) >= 0 ||
            slotId.EndsWith('.') ||
            slotId.EndsWith(' '))
        {
            throw new ArgumentException($"Invalid snapshot slot ID '{slotId}'.", nameof(slotId));
        }
    }

    public static bool TryParseSegmentId(string fileNameOrPath, out uint segmentId)
    {
        segmentId = 0;
        if (string.IsNullOrEmpty(fileNameOrPath))
            return false;

        int lastSlash = fileNameOrPath.LastIndexOfAny(['/', '\\']);
        ReadOnlySpan<char> fileName = lastSlash >= 0 ? fileNameOrPath.AsSpan(lastSlash + 1) : fileNameOrPath.AsSpan();

        const string prefix = "seg_";
        const string suffix = ".chk";
        if (fileName.Length < prefix.Length + 6 + suffix.Length ||
            !fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> digits = fileName.Slice(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
        for (int i = 0; i < digits.Length; i++)
        {
            if (!char.IsAsciiDigit(digits[i]))
                return false;
        }

        if (digits.Length > 6 && digits[0] == '0')
            return false;

        return uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out segmentId) && segmentId >= 1;
    }

    public static bool TryParseWorldGuid(string dirNameOrPath, out Guid worldGuid)
    {
        worldGuid = Guid.Empty;
        if (string.IsNullOrEmpty(dirNameOrPath))
            return false;

        ReadOnlySpan<char> span = dirNameOrPath.AsSpan().TrimEnd(['/', '\\']);
        int lastSlash = span.LastIndexOfAny(['/', '\\']);
        ReadOnlySpan<char> segment = lastSlash >= 0 ? span[(lastSlash + 1)..] : span;
        return segment.Length == 32 && Guid.TryParseExact(segment, "N", out worldGuid);
    }

    public static bool TryExtractSlotIdFromSnapshotPath(string fileNameOrPath, out string slotId)
    {
        slotId = string.Empty;
        if (string.IsNullOrEmpty(fileNameOrPath))
            return false;

        int lastSlash = fileNameOrPath.LastIndexOfAny(['/', '\\']);
        ReadOnlySpan<char> fileName = lastSlash >= 0 ? fileNameOrPath.AsSpan(lastSlash + 1) : fileNameOrPath.AsSpan();
        const string ext = ".snap";
        if (fileName.Length <= ext.Length || !fileName.EndsWith(ext, StringComparison.Ordinal))
            return false;

        slotId = fileName[..^ext.Length].ToString();
        return true;
    }

    private static char[] ComputeInvalidSlotChars()
    {
        char[] invalidFileChars = Path.GetInvalidFileNameChars();
        var merged = new char[invalidFileChars.Length + 2];
        invalidFileChars.CopyTo(merged, 0);
        merged[^2] = '/';
        merged[^1] = '\\';
        return merged;
    }
}
