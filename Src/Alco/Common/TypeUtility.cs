using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace Alco;

/// <summary>
/// Provides utility methods for working with types and type checking.
/// </summary>
public static class TypeUtility
{
    /// <summary>
    /// Determines whether the specified type is a <see cref="IList{T}"/>.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <param name="genericType">When this method returns, contains the generic type argument of the IList if the type is a generic IList; otherwise, null.</param>
    /// <returns>true if the specified type is a generic IList; otherwise, false.</returns>
    public static bool IsGenericList(Type type, [MaybeNullWhen(false)] out Type genericType)
    {
        if (type.IsGenericType &&
            (typeof(IList<>).IsAssignableFrom(type.GetGenericTypeDefinition()) ||
             type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>))))
        {
            genericType = type.GetGenericArguments()[0];
            return true;
        }
        genericType = null;
        return false;
    }

    /// <summary>
    /// Determines whether the specified type is a <see cref="ISet{T}"/>.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <param name="genericType">When this method returns, contains the generic type argument of the ISet if the type is a generic ISet; otherwise, null.</param>
    /// <returns>true if the specified type is a generic ISet; otherwise, false.</returns>
    public static bool IsGenericSet(Type type, [MaybeNullWhen(false)] out Type genericType)
    {
        if (type.IsGenericType &&
            (typeof(ISet<>).IsAssignableFrom(type.GetGenericTypeDefinition()) ||
             type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>))))
        {
            genericType = type.GetGenericArguments()[0];
            return true;
        }
        genericType = null;
        return false;
    }

    /// <summary>
    /// Determines whether the specified type is a <see cref="IDictionary{TKey, TValue}"/>.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <param name="genericKeyType">When this method returns, contains the generic key type argument of the Dictionary if the type is a generic Dictionary; otherwise, null.</param>
    /// <param name="genericValueType">When this method returns, contains the generic value type argument of the Dictionary if the type is a generic Dictionary; otherwise, null.</param>
    /// <returns>true if the specified type is a generic Dictionary; otherwise, false.</returns>
    public static bool IsGenericDictionary(Type type, [MaybeNullWhen(false)] out Type genericKeyType, [MaybeNullWhen(false)] out Type genericValueType)
    {
        if (type.IsGenericType &&
            (typeof(IDictionary<,>).IsAssignableFrom(type.GetGenericTypeDefinition()) ||
             type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>))))
        {
            genericKeyType = type.GetGenericArguments()[0];
            genericValueType = type.GetGenericArguments()[1];
            return true;
        }
        genericKeyType = null;
        genericValueType = null;
        return false;
    }

    /// <summary>
    /// Finds all types across the provided assemblies (or all loaded assemblies if none specified)
    /// that are marked with the specified attribute type.
    /// </summary>
    /// <typeparam name="TAttribute">The attribute type to search for.</typeparam>
    /// <param name="assemblies">Optional assemblies to limit the search scope.</param>
    /// <returns>An array of matching types.</returns>
    public static Type[] FindTypesWithAttribute<TAttribute>(params Assembly[] assemblies) where TAttribute : Attribute
    {
        IEnumerable<Assembly> source = assemblies != null && assemblies.Length > 0
            ? assemblies
            : AppDomain.CurrentDomain.GetAssemblies();

        List<Type> result = new List<Type>();
        HashSet<Type> unique = new HashSet<Type>();

        foreach (var asm in source)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch
            {
                continue;
            }

            for (int i = 0; i < types.Length; i++)
            {
                var t = types[i];
                if (t.GetCustomAttribute<TAttribute>() != null)
                {
                    if (unique.Add(t))
                    {
                        result.Add(t);
                    }
                }
            }
        }

        return result.ToArray();
    }
}
