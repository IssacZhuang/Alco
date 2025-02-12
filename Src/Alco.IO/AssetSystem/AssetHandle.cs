using System.Runtime.CompilerServices;

namespace Alco.IO;

internal class AssetHandle
{
    private WeakReference<object>? _weakReference;
    private object? _strongReference;
    private bool _isLoading = false;

    

    public event AssetAsyncLoadDelegate? OnLoadComplete;//on main thread
    //just keep reference when asyc load to avoid GC
    public object? tmpAsset;

    public bool IsLoading
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isLoading;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _isLoading = value;
    }


    public object? CachedAsset
    {
        get
        {
            if (_weakReference != null && _weakReference.TryGetTarget(out object? target))
            {
                return target;
            }

            return _strongReference;
        }
    }

    public void SetCache(object obj, AssetCacheMode cacheMode)
    {
        if (cacheMode == AssetCacheMode.Recyclable)
        {
            _weakReference = new WeakReference<object>(obj);
        }
        else if (cacheMode == AssetCacheMode.Persistent)
        {
            _strongReference = obj;
        }
        // else do nothing
    }

    public AssetHandle()
    {
        
    }

    public void DoLoadComplete(object asset, Exception? exception)
    {
        OnLoadComplete?.Invoke(asset, exception);
    }

    public void ResetLoadingState()
    {
        tmpAsset = null;
        _isLoading = false;
        OnLoadComplete = null;
    }
}