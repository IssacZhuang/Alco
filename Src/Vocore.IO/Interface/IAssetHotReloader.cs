namespace Vocore.IO;

public interface IAssetHotReloader
{
    bool TryHotReload(object asset, ReadOnlySpan<byte> data);
}