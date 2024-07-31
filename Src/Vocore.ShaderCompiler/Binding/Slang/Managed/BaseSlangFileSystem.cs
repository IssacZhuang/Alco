
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using static SlangSharp.UtilsSlangInterop;

namespace SlangSharp;

public unsafe abstract class BaseSlangFileSystem : IDisposable, ISlangFileSystemManaged
{
    private readonly ConcurrentDictionary<string, nint> _cache = new();
    private readonly SlangFileSystem* _handle;

    public ISlangFileSystem* Handle
    {
        get => (ISlangFileSystem*)_handle;
    }

    public BaseSlangFileSystem()
    {
        _handle = Alloc<SlangFileSystem>();
        *_handle = new SlangFileSystem(this);
    }

    public bool TryLoadFile(string path, out ISlangBlob* blob)
    {
        if (_cache.TryGetValue(path, out nint value))
        {
            blob = (ISlangBlob*)value;
            return true;
        }

        if (TryLoadFile(path, out byte[] data))
        {
            SlangBlob* slangBlob = Alloc<SlangBlob>();
            *slangBlob = new SlangBlob(data);
            blob = (ISlangBlob*)slangBlob;
            _cache.TryAdd(path, (nint)blob);
            return true;
        }
        blob = null;
        return false;
    }

    public void ReleaseCache(string path)
    {
        if (_cache.TryRemove(path, out nint value))
        {
            Free((void*)value);
        }
    }

    public void ReleaseAllCache()
    {
        foreach (var item in _cache)
        {
            Free((void*)item.Value);
        }
        _cache.Clear();
    }

    public void Dispose()
    {
        ReleaseAllCache();
        Free(_handle);
    }

    public abstract bool TryLoadFile(string path, out byte[] data);


}