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

        public bool TryGet(string key, out T target)
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

    public class WeakCache
    {
        private readonly Dictionary<string, WeakReference> _cache = new Dictionary<string, WeakReference>();

        public void Set(string key, object value)
        {
            if (_cache.TryGetValue(key, out var reference))
            {
                reference.Target = value;
            }
            else
            {
                _cache.Add(key, new WeakReference(value));
            }
        }

        public bool TryGet(string key, out object obj)
        {
            if (_cache.TryGetValue(key, out var reference))
            {
                if (reference.IsAlive)
                {
                    obj = reference.Target;
                    return true;
                }
                else
                {
                    _cache.Remove(key);
                }
            }
            obj = null;
            return false;
        }
    }
}

