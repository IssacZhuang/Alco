using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

public class ConcurrentWeakCache<TKey, TValue> where TKey : notnull where TValue : class
{
    private readonly ConcurrentDictionary<TKey, WeakReference> _cache = new ConcurrentDictionary<TKey, WeakReference>();

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        WeakReference reference = _cache.GetOrAdd(key, (k) => new WeakReference(valueFactory(k)));

        if (reference.Target is TValue value)
        {
            return value;
        }

        lock (reference)
        {
            if (reference.Target is not TValue value2)
            {
                value = valueFactory(key);
                reference.Target = value;
                return value;
            }

            return value2;
        }

    }
}


