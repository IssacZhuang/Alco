using System.Diagnostics.CodeAnalysis;
using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// File source that serves exactly one file under a root directory. Used to mount
/// file-level referenced asset entries (<see cref="AlcoProject.ReferencedAssets"/>)
/// without exposing sibling files.
/// </summary>
public sealed class SingleFileSource : IFileSource
{
    private readonly string _rootDirectory;
    private readonly string _relativePath;

    /// <summary>
    /// Creates a source serving <paramref name="relativePath"/> under <paramref name="rootDirectory"/>.
    /// </summary>
    /// <param name="rootDirectory">Absolute root directory the relative path resolves against.</param>
    /// <param name="relativePath">The single served path, relative to the root ('\' is normalized to '/').</param>
    public SingleFileSource(string rootDirectory, string relativePath)
    {
        _rootDirectory = rootDirectory;
        _relativePath = AlcoProject.NormalizeEntry(relativePath);
    }

    /// <summary>The absolute path of the served file.</summary>
    public string AbsolutePath => Path.Combine(_rootDirectory, _relativePath);

    /// <inheritdoc/>
    public string Name => AbsolutePath;

    /// <inheritdoc/>
    public int Priority => 5;

    /// <inheritdoc/>
    public IEnumerable<string> AllFileNames
    {
        get
        {
            if (File.Exists(AbsolutePath))
            {
                yield return _relativePath;
            }
        }
    }

    /// <inheritdoc/>
    public unsafe bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, [NotNullWhen(false)] out string? failureReason)
    {
        if (!Matches(path))
        {
            data = SafeMemoryHandle.Empty;
            failureReason = $"File '{path}' is not served by this source.";
            return false;
        }
        try
        {
            byte* ptr = UnsafeIO.ReadFile(AbsolutePath, out int size);
            data = new SafeMemoryHandle(ptr, size);
            failureReason = string.Empty;
            return true;
        }
        catch (Exception e)
        {
            data = SafeMemoryHandle.Empty;
            failureReason = e.ToString();
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryGetStream(string path, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? failureReason)
    {
        if (!Matches(path))
        {
            stream = null;
            failureReason = $"File '{path}' is not served by this source.";
            return false;
        }
        try
        {
            stream = File.OpenRead(AbsolutePath);
            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            stream = null;
            failureReason = e.ToString();
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // nothing to release
    }

    private bool Matches(string path)
    {
        return string.Equals(AlcoProject.NormalizeEntry(path), _relativePath, StringComparison.OrdinalIgnoreCase);
    }
}
