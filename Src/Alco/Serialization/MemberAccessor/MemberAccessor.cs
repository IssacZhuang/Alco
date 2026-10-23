//Modified from .net 9 System.Text.Json

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// Creates delegates for constructing types and for reading and writing their members.
/// </summary>
public abstract class MemberAccessor
{
    internal const string SerializationUnreferencedCodeMessage = "JSON serialization and deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo or JsonSerializerContext, or make sure all of the required types are preserved.";
    internal const string SerializationRequiresDynamicCodeMessage = "JSON serialization and deserialization might require types that cannot be statically analyzed and might need runtime code generation. Use System.Text.Json source generation for native AOT applications.";
    internal static readonly Type ObjectType = typeof(object);

    /// <summary>
    /// Creates a delegate that invokes the parameterless constructor of the specified type.
    /// </summary>
    /// <param name="type">The type to construct.</param>
    /// <param name="constructorInfo">The constructor to invoke, or null to use default value type construction.</param>
    /// <returns>A constructor delegate, or null when the type cannot be constructed.</returns>
    public abstract Func<object>? CreateParameterlessConstructor(Type type, ConstructorInfo? constructorInfo);

    /// <summary>
    /// Creates a delegate that invokes the specified constructor with an argument array.
    /// </summary>
    /// <param name="constructor">The constructor to invoke.</param>
    /// <returns>A delegate that constructs a new instance of <typeparamref name="T"/>.</returns>
    public abstract Func<object[], T> CreateParameterizedConstructor<T>(ConstructorInfo constructor);

    /// <summary>
    /// Creates a delegate that invokes the specified constructor with up to four typed arguments.
    /// </summary>
    /// <param name="constructor">The constructor to invoke.</param>
    /// <returns>A constructor delegate, or null when the constructor cannot be invoked this way.</returns>
    public abstract ParameterizedConstructorDelegate<T, TArg0, TArg1, TArg2, TArg3>? CreateParameterizedConstructor<T, TArg0, TArg1, TArg2, TArg3>(ConstructorInfo constructor);

    /// <summary>
    /// Creates a delegate that adds an element to the collection.
    /// </summary>
    /// <typeparam name="TCollection">The collection type.</typeparam>
    /// <returns>A delegate that adds an element to the collection.</returns>
    public abstract Action<TCollection, object?> CreateAddMethodDelegate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TCollection>();

    /// <summary>
    /// Creates a delegate that builds an immutable collection from a sequence of elements.
    /// </summary>
    /// <typeparam name="TCollection">The immutable collection type.</typeparam>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>A delegate that creates the collection.</returns>
    public abstract Func<IEnumerable<TElement>, TCollection> CreateImmutableEnumerableCreateRangeDelegate<TCollection, TElement>();

    /// <summary>
    /// Creates a delegate that builds an immutable dictionary from key-value pairs.
    /// </summary>
    /// <typeparam name="TCollection">The immutable dictionary type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <returns>A delegate that creates the dictionary.</returns>
    public abstract Func<IEnumerable<KeyValuePair<TKey, TValue>>, TCollection> CreateImmutableDictionaryCreateRangeDelegate<TCollection, TKey, TValue>();

    /// <summary>
    /// Creates a delegate that reads the specified property.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="propertyInfo">The property to read.</param>
    /// <returns>A delegate that returns the property value of an instance.</returns>
    public abstract Func<object, TProperty> CreatePropertyGetter<TProperty>(PropertyInfo propertyInfo);

    /// <summary>
    /// Creates a delegate that writes the specified property.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="propertyInfo">The property to write.</param>
    /// <returns>A delegate that sets the property value on an instance.</returns>
    public abstract Action<object, TProperty> CreatePropertySetter<TProperty>(PropertyInfo propertyInfo);

    /// <summary>
    /// Creates a delegate that reads the specified field.
    /// </summary>
    /// <typeparam name="TProperty">The field type.</typeparam>
    /// <param name="fieldInfo">The field to read.</param>
    /// <returns>A delegate that returns the field value of an instance.</returns>
    public abstract Func<object, TProperty> CreateFieldGetter<TProperty>(FieldInfo fieldInfo);

    /// <summary>
    /// Creates a delegate that writes the specified field.
    /// </summary>
    /// <typeparam name="TProperty">The field type.</typeparam>
    /// <param name="fieldInfo">The field to write.</param>
    /// <returns>A delegate that sets the field value on an instance.</returns>
    public abstract Action<object, TProperty> CreateFieldSetter<TProperty>(FieldInfo fieldInfo);

    /// <summary>
    /// Clears any delegates cached by this accessor.
    /// </summary>
    public virtual void Clear() { }

    /// <summary>
    /// Creates an accessor that uses reflection emit when dynamic code is supported, otherwise plain reflection.
    /// </summary>
    /// <returns>The created member accessor.</returns>
    public static MemberAccessor CreateCompatibleAccessor()
    {
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            return new ReflectionEmitMemberAccessor();
        }
        return new ReflectionMemberAccessor();
    }

    /// <summary>
    /// Creates a compatible accessor wrapped in a <see cref="MemberAccessorCache"/>.
    /// </summary>
    /// <returns>The cached member accessor.</returns>
    public static MemberAccessor CreateCompatibleCachedAccessor()
    {
        MemberAccessor accessor = CreateCompatibleAccessor();
        return new MemberAccessorCache(accessor);
    }
}

