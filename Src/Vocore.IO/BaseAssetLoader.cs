using System.Diagnostics.CodeAnalysis;

namespace Vocore.IO;

public abstract class BaseAssetLoader<TAsset> : IAssetLoader<TAsset> where TAsset : class
{
    public abstract string Name { get; }

    public abstract IReadOnlyList<string> FileExtensions { get; }

    protected abstract bool TryCreateAssetCore(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out TAsset? asset);


    /// <summary>
    /// Tries to create an asset from the file. It might be executed on a background thread so it has to be thread-safe.
    /// </summary>
    /// <param name="filename">The filename of the asset.</param>
    /// <param name="file">The file data.</param>
    /// <param name="asset">When this method returns, contains the created asset if the asset creation was successful; otherwise, <c>null</c>.</param>
    /// <returns><c>true</c> if the asset was successfully created; otherwise, <c>false</c>.</returns>
    public bool TryCreateAsset(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out TAsset? asset)
    {
        return TryCreateAssetCore(filename, file, out asset);
    }
}