namespace Vocore.IO;

internal class AssetHandle
{
    private WeakReference<object>? _weakReference;
    private object? _strongReference;
    public event Action<object>? OnLoad;//might be async
    public event Action<object>? OnLoadComplete;//on main thread

    public AssetHandle()
    {
        
    }
}