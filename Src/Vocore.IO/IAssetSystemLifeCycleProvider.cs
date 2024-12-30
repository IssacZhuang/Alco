namespace Vocore.IO;

public interface IAssetSystemLifeCycleProvider
{
    event Action OnHandleAssetLoaded;
    event Action OnDispose;
}
