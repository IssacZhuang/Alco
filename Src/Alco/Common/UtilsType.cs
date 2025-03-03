using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

/// <summary>
/// Provides utility methods for working with types and type checking.
/// </summary>
public static class UtilsType
{
    /// <summary>
    /// Determines whether the specified type is a <see cref="IList{T}"/>.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <param name="genericType">When this method returns, contains the generic type argument of the IList if the type is a generic IList; otherwise, null.</param>
    /// <returns>true if the specified type is a generic IList; otherwise, false.</returns>
    public static bool IsGenericList(Type type, [MaybeNullWhen(false)] out Type genericType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>))
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
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>))
        {
            genericKeyType = type.GetGenericArguments()[0];
            genericValueType = type.GetGenericArguments()[1];
            return true;
        }
        genericKeyType = null;
        genericValueType = null;
        return false;
    }
}
