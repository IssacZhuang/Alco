namespace Alco.IO;

public abstract class BaseAssetEncoder<T> : IAssetEncoder
{
    public IEnumerable<Type> GetSupportedTypes()
    {
        yield return typeof(T);
    }

    public abstract SafeMemoryHandle Encode(object asset);
}

