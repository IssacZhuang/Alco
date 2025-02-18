namespace Alco.IO;

public class StorageSystem
{
    private readonly IStorageSystemHost _host;

    public StorageSystem(IStorageSystemHost host)
    {
        _host = host;
        _host.OnDispose += Dispose;
    }

    private void Dispose()
    {

    }
}
