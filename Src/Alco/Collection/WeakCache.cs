using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

public class WeakCache<TKey, TValue> where TKey : notnull where TValue : class
{
    private readonly Dictionary<TKey, WeakReference<TValue>> _cache = new Dictionary<TKey, WeakReference<TValue>>();

    public void Set(TKey key, TValue value)
    {
        if (_cache.TryGetValue(key, out var reference))
        {
            reference.SetTarget(value);
        }
        else
        {
            _cache.Add(key, new WeakReference<TValue>(value));
        }
    }

    public bool TryGet(TKey key, [NotNullWhen(true)] out TValue? target)
    {
        if (_cache.TryGetValue(key, out var reference))
        {
            if (reference.TryGetTarget(out var result))
            {
                target = result;
                return true;
            }
            else
            {
                _cache.Remove(key);
            }
        }
        target = null;
        return false;
    }
}


