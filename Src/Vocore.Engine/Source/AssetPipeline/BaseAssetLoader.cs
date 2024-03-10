using System.Diagnostics.CodeAnalysis;

namespace Vocore.Engine;

public abstract class BaseAssetLoader<TAsset, TPreprocessed> : IAssetLoader<TAsset> where TAsset : class
{
    public abstract string Name { get; }

    public abstract IEnumerable<string> FileExtensions { get; }

    protected virtual TPreprocessed AsyncPreprocess(string filename, byte[] data)
    {
        throw new NotImplementedException();
    }

    protected virtual bool Load(string filename, TPreprocessed preprocessed, [NotNullWhen(true)] out TAsset? asset)
    {
        throw new NotImplementedException();
    }

    public object OnAsyncPreprocess(string filename, byte[] data)
    {
        object? preprocessed = AsyncPreprocess(filename, data);

        if (preprocessed == null)
        {
            throw new Exception("Failed to preprocess asset");
        }

        return preprocessed;
    }

    public bool OnLoad(string filename, object preprocessed, [NotNullWhen(true)] out TAsset? asset)
    {
        return Load(filename, (TPreprocessed)preprocessed, out asset);
    }
}