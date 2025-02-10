using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace Alco;

public class TypeHelper
{
    private readonly ConcurrentDictionary<string, Type> _typeCache = new ConcurrentDictionary<string, Type>();
    private readonly HashSet<Assembly> _assemblies = new HashSet<Assembly>();

    public TypeHelper()
    {
        // Add System assembly by default
        _assemblies.Add(typeof(int).Assembly);
        //add self assembly by default
        _assemblies.Add(typeof(TypeHelper).Assembly);
    }

    public TypeHelper(params Assembly[] assemblies) : this()
    {
        AddAssemblies(assemblies);
    }

    /// <summary>
    /// Add assemblies to search for types.
    /// </summary>
    /// <param name="assemblies">The assemblies to add.</param>
    public void AddAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            _assemblies.Add(assembly);
        }
    }

    /// <summary>
    /// Tries to find a type by its name in the registered assemblies.
    /// </summary>
    /// <param name="typeName">The full name of the type to find.</param>
    /// <returns>The found Type, or null if not found.</returns>
    public Type? FindType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return null;
        }

        // Try to get from cache first
        if (_typeCache.TryGetValue(typeName, out Type? cachedType))
        {
            return cachedType;
        }

        // Search in registered assemblies
        foreach (var assembly in _assemblies)
        {
            Type? type = assembly.GetType(typeName);
            if (type != null)
            {
                _typeCache.TryAdd(typeName, type);
                return type;
            }
        }

        return null;
    }

    /// <summary>
    /// Clears the type cache.
    /// </summary>
    public void ClearCache()
    {
        _typeCache.Clear();
    }
}



