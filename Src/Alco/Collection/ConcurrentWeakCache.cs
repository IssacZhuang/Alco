using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco
{
    public class ConcurrentWeakCache<T> where T : class
    {
        private readonly ConcurrentDictionary<string, WeakReference<T>> _cache = new ConcurrentDictionary<string, WeakReference<T>>();

        public void Set(string key, T value)
        {
            if (_cache.TryGetValue(key, out var reference))
            {
                reference.SetTarget(value);
            }
            else
            {
                _cache[key] = new WeakReference<T>(value);
            }
        }

        public bool TryGet(string key, [NotNullWhen(true)] out T? target)
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
                    _cache.TryRemove(key, out var _);
                }
            }
            target = null;
            return false;
        }
    }
}

