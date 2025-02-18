namespace Alco.IO;

public interface IAssetHotReloader
{
    IEnumerable<Type> GetSupportedTypes();
    void HotReload(object asset, ReadOnlySpan<byte> data);
}