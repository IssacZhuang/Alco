using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

/// <summary>
/// A thread-safe keyed cache that holds values through weak references so unused entries can be collected.
/// </summary>
/// <typeparam name="TKey">The cache key type.</typeparam>
/// <typeparam name="TValue">The cached reference type.</typeparam>
public class ConcurrentWeakCache<TKey, TValue> where TKey : notnull where TValue : class
{
    private readonly ConcurrentDictionary<TKey, WeakReference> _cache = new ConcurrentDictionary<TKey, WeakReference>();

    /// <summary>
    /// Gets the live cached value for <paramref name="key"/>, creating it with <paramref name="valueFactory"/> when absent or collected.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="valueFactory">Creates the value when no live cached instance exists.</param>
    /// <returns>The cached or newly created value.</returns>
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


