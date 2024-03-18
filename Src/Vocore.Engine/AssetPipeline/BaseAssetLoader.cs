using System.Diagnostics.CodeAnalysis;

namespace Vocore.Engine;

public abstract class BaseAssetLoader<TAsset, TPreprocessed> : IAssetLoader<TAsset> where TAsset : class
{
    public abstract string Name { get; }

    public abstract IReadOnlyList<string> FileExtensions { get; }

    protected abstract bool TryAsyncPreprocessCore(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out TPreprocessed? preprocessed);

    protected abstract bool TryCreateAssetCore(string filename, TPreprocessed preprocessed, [NotNullWhen(true)] out TAsset? asset);

    
    /// <summary>
    /// Tries to asynchronously preprocess the specified file. It will execute asynchronously when using AssetManager.LoadAsync.
    /// </summary>
    /// <param name="filename">The name of the file.</param>
    /// <param name="file">The file content as a byte array.</param>
    /// <param name="preprocessed">When this method returns, contains the preprocessed object if the preprocessing was successful; otherwise, <c>null</c>.</param>
    /// <returns><c>true</c> if the preprocessing was successful; otherwise, <c>false</c>.</returns>
    public bool TryAsyncPreprocess(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out object? preprocessed)
    {
        if (TryAsyncPreprocessCore(filename, file, out var p))
        {
            preprocessed = p;
            return true;
        }
        preprocessed = null;
        return false;
    }

    
    /// <summary>
    /// Tries to create an asset from the specified filename and preprocessed data. This method will execute on main thread when preprocessed data is ready.
    /// </summary>
    /// <param name="filename">The filename of the asset.</param>
    /// <param name="preprocessed">The preprocessed data.</param>
    /// <param name="asset">When this method returns, contains the created asset if the asset creation was successful; otherwise, <c>null</c>.</param>
    /// <returns><c>true</c> if the asset was successfully created; otherwise, <c>false</c>.</returns>
    public bool TryCreateAsset(string filename, object preprocessed, [NotNullWhen(true)] out TAsset? asset)
    {
        return TryCreateAssetCore(filename, (TPreprocessed)preprocessed, out asset);
    }
}