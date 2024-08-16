using System.Runtime.CompilerServices;

namespace Vocore.IO;

internal class AssetHandle
{
    private WeakReference<object>? _weakReference;
    private object? _strongReference;
    private bool _isLoading = false;

    public Func<object>? FuncLoad;//might be async
    //todo: add error handling in the load complete callback
    public event Action<object>? OnLoadComplete;//on main thread

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

    public void DoLoadComplete(object asset)
    {
        OnLoadComplete?.Invoke(asset);
    }

    public void ResetLoadingState()
    {
        _isLoading = false;
        OnLoadComplete = null;
    }
}