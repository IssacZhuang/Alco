using System.Runtime.CompilerServices;

namespace Alco.IO;

internal sealed class AssetHandle
{
    private volatile WeakReference<object>? _weakReference;
    private bool _isLoading = false;

    public bool IsLoading
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isLoading;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _isLoading = value;
    }

    public object? CachedAsset
        => _weakReference != null && _weakReference.TryGetTarget(out object? target) ? target : null;

    public void SetCache(object obj)
    {
        WeakReference<object>? weakRef = _weakReference;
        if (weakRef == null)
        {
            _weakReference = new WeakReference<object>(obj);
        }
        else
        {
            weakRef.SetTarget(obj);
        }
    }

    public void ResetLoadingState()
    {
        _isLoading = false;
    }
}