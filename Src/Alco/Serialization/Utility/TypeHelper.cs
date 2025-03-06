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

    /// <summary>
    /// Search types from all assemblies in the app domain.
    /// </summary>
    public bool UseGlobalAssemblies { get; set; } = true;

    public TypeHelper(bool useGlobalAssemblies = false)
    {
        // Add System assembly by default
        _assemblies.Add(typeof(int).Assembly);
        //add self assembly by default
        _assemblies.Add(typeof(TypeHelper).Assembly);
        //add executing assembly by default
        _assemblies.Add(Assembly.GetExecutingAssembly());
        UseGlobalAssemblies = useGlobalAssemblies;
    }

    public TypeHelper(params ReadOnlySpan<Assembly> assemblies) : this()
    {
        AddAssemblies(assemblies);
    }

    /// <summary>
    /// Add assemblies to search for types.
    /// </summary>
    /// <param name="assemblies">The assemblies to add.</param>
    public void AddAssemblies(params ReadOnlySpan<Assembly> assemblies)
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

        if (UseGlobalAssemblies)
        {
            // Search in all assemblies in the app domain
            Type? type = FindTypeFromGlobalAssemblies(typeName);
            if (type != null)
            {
                _typeCache.TryAdd(typeName, type);
                return type;
            }
        }
        else
        {
            // Search in registered assemblies
            Type? type = FindTypeFromRegisteredAssemblies(typeName);
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

    private Type? FindTypeFromGlobalAssemblies(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
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

    private Type? FindTypeFromRegisteredAssemblies(string typeName)
    {
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
}



