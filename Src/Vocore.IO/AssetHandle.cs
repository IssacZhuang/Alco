namespace Vocore.IO;

internal class AssetHandle
{
    private WeakReference<object>? _ref;
    public event Action<object>? OnLoad;//might be async
    public event Action<object>? OnLoadComplete;//on main thread

    public AssetHandle()
    {
        
    }
}