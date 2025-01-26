using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco
{
    public class WeakCache<T> where T : class
    {
        private readonly Dictionary<string, WeakReference<T>> _cache = new Dictionary<string, WeakReference<T>>();

        public void Set(string key, T value)
        {
            if (_cache.TryGetValue(key, out var reference))
            {
                reference.SetTarget(value);
            }
            else
            {
                _cache.Add(key, new WeakReference<T>(value));
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
                    _cache.Remove(key);
                }
            }
            target = null;
            return false;
        }
    }
}

