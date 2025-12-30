using System.Runtime.CompilerServices;

namespace Alco.IO;

internal class AssetHandle
{
    private WeakReference? _weakReference;
    private object? _strongReference;
    private bool _isLoading = false;

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
            if (_weakReference != null && _weakReference.IsAlive)
            {
                return _weakReference.Target;
            }

            return _strongReference;
        }
    }

    public void SetCache(object obj, AssetCacheMode cacheMode)
    {
        if (cacheMode == AssetCacheMode.Recyclable)
        {
            _weakReference = new WeakReference(obj);
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

    public void ResetLoadingState()
    {
        _isLoading = false;
    }
}