using System;
using System.Collections.Generic;

namespace Vocore
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

        public T Get(string key)
        {
            if (_cache.TryGetValue(key, out var reference))
            {
                if (reference.TryGetTarget(out var result))
                {
                    return result;
                }
                else
                {
                    _cache.Remove(key);
                }
            }
            return null;
        }
    }
}

