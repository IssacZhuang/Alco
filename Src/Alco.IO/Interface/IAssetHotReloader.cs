namespace Alco.IO;

public interface IAssetHotReloader
{
    void HotReload(object asset, ReadOnlySpan<byte> data);
}