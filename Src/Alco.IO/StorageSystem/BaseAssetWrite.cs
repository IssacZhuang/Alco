namespace Alco.IO;

public abstract class BaseAssetWriter<T> : IAssetWriter
{
    public IEnumerable<Type> GetSupportedTypes()
    {
        yield return typeof(T);
    }

    public abstract SafeMemoryHandle Write(object asset, out string path);
}

