using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace Alco;

public class TypeHelper
{
    public static readonly TypeHelper Default = new TypeHelper(true);

    private readonly ConcurrentDictionary<string, Type?> _typeCache = new ConcurrentDictionary<string, Type?>();
    private readonly HashSet<Assembly> _assemblies = new HashSet<Assembly>();

    /// <summary>
    /// Search types from all assemblies in the app domain.
    /// </summary>
    public bool UseGlobalAssemblies { get; set; } = true;

    public TypeHelper(bool useGlobalAssemblies = false)
    {
        if (!useGlobalAssemblies)
        {
            // Add System assembly by default
            _assemblies.Add(typeof(int).Assembly);
            //add self assembly by default
            _assemblies.Add(typeof(TypeHelper).Assembly);
            //add executing assembly by default
            _assemblies.Add(Assembly.GetExecutingAssembly());
        }

        UseGlobalAssemblies = useGlobalAssemblies;
    }

    public TypeHelper(params ReadOnlySpan<Assembly> assemblies)
    {
        // Add System assembly by default
        _assemblies.Add(typeof(int).Assembly);
        //add self assembly by default
        _assemblies.Add(typeof(TypeHelper).Assembly);
        //add executing assembly by default
        _assemblies.Add(Assembly.GetExecutingAssembly());
        foreach (var assembly in assemblies)
        {
            _assemblies.Add(assembly);
        }
        UseGlobalAssemblies = false;
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

        if (UseGlobalAssemblies)
        {
            return _typeCache.GetOrAdd(typeName, FindTypeFromGlobalAssemblies(typeName));
        }
        else
        {
            return _typeCache.GetOrAdd(typeName, FindTypeFromRegisteredAssemblies(typeName));
        }
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



