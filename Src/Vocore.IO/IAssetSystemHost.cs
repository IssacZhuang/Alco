namespace Vocore.IO;

public interface IAssetSystemHost
{
    event Action OnHandleAssetLoaded;
    event Action OnDispose;
}
