//Modified from .net 9 System.Text.Json

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection;
using System.Collections.Generic;
using System;

#if !BUILDING_SOURCE_GENERATOR
using System.Diagnostics.CodeAnalysis;
#endif

namespace Alco;

internal static partial class ReflectionExtensions
{
    // Immutable collection types.
    private const string ImmutableArrayGenericTypeName = "System.Collections.Immutable.ImmutableArray`1";
    private const string ImmutableListGenericTypeName = "System.Collections.Immutable.ImmutableList`1";
    private const string ImmutableListGenericInterfaceTypeName = "System.Collections.Immutable.IImmutableList`1";
    private const string ImmutableStackGenericTypeName = "System.Collections.Immutable.ImmutableStack`1";
    private const string ImmutableStackGenericInterfaceTypeName = "System.Collections.Immutable.IImmutableStack`1";
    private const string ImmutableQueueGenericTypeName = "System.Collections.Immutable.ImmutableQueue`1";
    private const string ImmutableQueueGenericInterfaceTypeName = "System.Collections.Immutable.IImmutableQueue`1";
    private const string ImmutableSortedSetGenericTypeName = "System.Collections.Immutable.ImmutableSortedSet`1";
    private const string ImmutableHashSetGenericTypeName = "System.Collections.Immutable.ImmutableHashSet`1";
    private const string ImmutableSetGenericInterfaceTypeName = "System.Collections.Immutable.IImmutableSet`1";
    private const string ImmutableDictionaryGenericTypeName = "System.Collections.Immutable.ImmutableDictionary`2";
    private const string ImmutableDictionaryGenericInterfaceTypeName = "System.Collections.Immutable.IImmutableDictionary`2";
    private const string ImmutableSortedDictionaryGenericTypeName = "System.Collections.Immutable.ImmutableSortedDictionary`2";

    // Immutable collection builder types.
    private const string ImmutableArrayTypeName = "System.Collections.Immutable.ImmutableArray";
    private const string ImmutableListTypeName = "System.Collections.Immutable.ImmutableList";
    private const string ImmutableStackTypeName = "System.Collections.Immutable.ImmutableStack";
    private const string ImmutableQueueTypeName = "System.Collections.Immutable.ImmutableQueue";
    private const string ImmutableSortedSetTypeName = "System.Collections.Immutable.ImmutableSortedSet";
    private const string ImmutableHashSetTypeName = "System.Collections.Immutable.ImmutableHashSet";
    private const string ImmutableDictionaryTypeName = "System.Collections.Immutable.ImmutableDictionary";
    private const string ImmutableSortedDictionaryTypeName = "System.Collections.Immutable.ImmutableSortedDictionary";

    public const string CreateRangeMethodName = "CreateRange";


    public static bool IsImmutableDictionaryType(this Type type)
    {
        if (!type.IsGenericType || !type.Assembly.FullName!.StartsWith("System.Collections.Immutable", StringComparison.Ordinal))
        {
            return false;
        }

        switch (GetBaseNameFromGenericType(type))
        {
            case ImmutableDictionaryGenericTypeName:
            case ImmutableDictionaryGenericInterfaceTypeName:
            case ImmutableSortedDictionaryGenericTypeName:
                return true;
            default:
                return false;
        }
    }

    public static bool IsImmutableEnumerableType(this Type type)
    {
        if (!type.IsGenericType || !type.Assembly.FullName!.StartsWith("System.Collections.Immutable", StringComparison.Ordinal))
        {
            return false;
        }

        switch (GetBaseNameFromGenericType(type))
        {
            case ImmutableArrayGenericTypeName:
            case ImmutableListGenericTypeName:
            case ImmutableListGenericInterfaceTypeName:
            case ImmutableStackGenericTypeName:
            case ImmutableStackGenericInterfaceTypeName:
            case ImmutableQueueGenericTypeName:
            case ImmutableQueueGenericInterfaceTypeName:
            case ImmutableSortedSetGenericTypeName:
            case ImmutableHashSetGenericTypeName:
            case ImmutableSetGenericInterfaceTypeName:
                return true;
            default:
                return false;
        }
    }

    public static string? GetImmutableDictionaryConstructingTypeName(this Type type)
    {
        Debug.Assert(type.IsImmutableDictionaryType());

        // Use the generic type definition of the immutable collection to determine
        // an appropriate constructing type, i.e. a type that we can invoke the
        // `CreateRange<T>` method on, which returns the desired immutable collection.
        switch (GetBaseNameFromGenericType(type))
        {
            case ImmutableDictionaryGenericTypeName:
            case ImmutableDictionaryGenericInterfaceTypeName:
                return ImmutableDictionaryTypeName;
            case ImmutableSortedDictionaryGenericTypeName:
                return ImmutableSortedDictionaryTypeName;
            default:
                // We verified that the type is an immutable collection, so the
                // generic definition is one of the above.
                return null;
        }
    }

    public static string? GetImmutableEnumerableConstructingTypeName(this Type type)
    {
        Debug.Assert(type.IsImmutableEnumerableType());

        // Use the generic type definition of the immutable collection to determine
        // an appropriate constructing type, i.e. a type that we can invoke the
        // `CreateRange<T>` method on, which returns the desired immutable collection.
        switch (GetBaseNameFromGenericType(type))
        {
            case ImmutableArrayGenericTypeName:
                return ImmutableArrayTypeName;
            case ImmutableListGenericTypeName:
            case ImmutableListGenericInterfaceTypeName:
                return ImmutableListTypeName;
            case ImmutableStackGenericTypeName:
            case ImmutableStackGenericInterfaceTypeName:
                return ImmutableStackTypeName;
            case ImmutableQueueGenericTypeName:
            case ImmutableQueueGenericInterfaceTypeName:
                return ImmutableQueueTypeName;
            case ImmutableSortedSetGenericTypeName:
                return ImmutableSortedSetTypeName;
            case ImmutableHashSetGenericTypeName:
            case ImmutableSetGenericInterfaceTypeName:
                return ImmutableHashSetTypeName;
            default:
                // We verified that the type is an immutable collection, so the
                // generic definition is one of the above.
                return null;
        }
    }

    private static string GetBaseNameFromGenericType(Type genericType)
    {
        Type genericTypeDef = genericType.GetGenericTypeDefinition();
        return genericTypeDef.FullName!;
    }
}

