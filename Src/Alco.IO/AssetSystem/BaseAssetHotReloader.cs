
namespace Alco.IO;

public abstract class BaseAssetHotReloader<T> : IAssetHotReloader
{
    public IEnumerable<Type> GetSupportedTypes()
    {
        yield return typeof(T);
    }

    public abstract void HotReload(object asset, ReadOnlySpan<byte> data);
}

