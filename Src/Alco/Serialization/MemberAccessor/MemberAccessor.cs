//Modified from .net 9 System.Text.Json

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Alco;

public abstract class MemberAccessor
{
    internal const string SerializationUnreferencedCodeMessage = "JSON serialization and deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo or JsonSerializerContext, or make sure all of the required types are preserved.";
    internal const string SerializationRequiresDynamicCodeMessage = "JSON serialization and deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use System.Text.Json source generation for native AOT applications.";
    internal static readonly Type ObjectType = typeof(object);

    public abstract Func<object>? CreateParameterlessConstructor(Type type, ConstructorInfo? constructorInfo);

    public abstract Func<object[], T> CreateParameterizedConstructor<T>(ConstructorInfo constructor);

    public abstract ParameterizedConstructorDelegate<T, TArg0, TArg1, TArg2, TArg3>? CreateParameterizedConstructor<T, TArg0, TArg1, TArg2, TArg3>(ConstructorInfo constructor);

    public abstract Action<TCollection, object?> CreateAddMethodDelegate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TCollection>();

    public abstract Func<IEnumerable<TElement>, TCollection> CreateImmutableEnumerableCreateRangeDelegate<TCollection, TElement>();

    public abstract Func<IEnumerable<KeyValuePair<TKey, TValue>>, TCollection> CreateImmutableDictionaryCreateRangeDelegate<TCollection, TKey, TValue>();

    public abstract Func<object, TProperty> CreatePropertyGetter<TProperty>(PropertyInfo propertyInfo);

    public abstract Action<object, TProperty> CreatePropertySetter<TProperty>(PropertyInfo propertyInfo);

    public abstract Func<object, TProperty> CreateFieldGetter<TProperty>(FieldInfo fieldInfo);

    public abstract Action<object, TProperty> CreateFieldSetter<TProperty>(FieldInfo fieldInfo);

    public virtual void Clear() { }

    public static MemberAccessor CreateCompatibleAccessor()
    {
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            return new ReflectionEmitMemberAccessor();
        }
        return new ReflectionMemberAccessor();
    }

    public static MemberAccessor CreateCompatibleCachedAccessor()
    {
        MemberAccessor accessor = CreateCompatibleAccessor();
        return new MemberAccessorCache(accessor);
    }
}

