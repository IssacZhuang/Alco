using System.Diagnostics.CodeAnalysis;

namespace Vocore.Engine;

public abstract class BaseAssetLoader<TAsset, TPreprocessed> : IAssetLoader<TAsset> where TAsset : class
{
    public abstract string Name { get; }

    public abstract IReadOnlyList<string> FileExtensions { get; }

    protected abstract bool TryAsyncPreprocessCore(string filename, byte[] file, [NotNullWhen(true)] out TPreprocessed? preprocessed);

    protected abstract bool TryCreateAssetCore(string filename, TPreprocessed preprocessed, [NotNullWhen(true)] out TAsset? asset);

    public bool TryAsyncPreprocess(string filename, byte[] file, [NotNullWhen(true)] out object? preprocessed)
    {
        if (TryAsyncPreprocessCore(filename, file, out var p))
        {
            preprocessed = p;
            return true;
        }
        preprocessed = null;
        return false;
    }

    public bool TryCreateAsset(string filename, object preprocessed, [NotNullWhen(true)] out TAsset? asset)
    {
        return TryCreateAssetCore(filename, (TPreprocessed)preprocessed, out asset);
    }
}