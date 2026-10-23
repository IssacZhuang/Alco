using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using CacheKey = (string id, System.Type declaringType, System.Reflection.MemberInfo? member);

namespace Alco;

/// <summary>
/// A member accessor that caches the delegates created by another accessor.
/// </summary>
public class MemberAccessorCache : MemberAccessor
{
    private readonly ConcurrentLruCache<CacheKey, object> _cache;
    private readonly MemberAccessor _accessor;

    /// <summary>
    /// Initializes a cached accessor that wraps the specified accessor.
    /// </summary>
    /// <param name="accessor">The accessor that creates uncached delegates.</param>
    /// <param name="capacity">The maximum number of cached delegates.</param>
    public MemberAccessorCache(MemberAccessor accessor, int capacity = 32)
    {
        _accessor = accessor;
        _cache = new(capacity);
    }

    /// <summary>
    /// Clears all cached delegates.
    /// </summary>
    public override void Clear() => _cache.Clear();

    /// <summary>
    /// Gets or creates a cached delegate that adds an element to the collection.
    /// </summary>
    public override Action<TCollection, object?> CreateAddMethodDelegate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TCollection>()
    {
        return GetOrAdd<Action<TCollection, object?>>(
            key: (nameof(CreateAddMethodDelegate), typeof(TCollection), null),
            valueFactory: _ => _accessor.CreateAddMethodDelegate<TCollection>());
    }

    /// <summary>
    /// Gets or creates a cached delegate that reads the specified field.
    /// </summary>
    public override Func<object, TProperty> CreateFieldGetter<TProperty>(FieldInfo fieldInfo)
    {
        return GetOrAdd<Func<object, TProperty>>(
            key: (nameof(CreateFieldGetter), typeof(TProperty), fieldInfo),
            valueFactory: key => _accessor.CreateFieldGetter<TProperty>((FieldInfo)key.member!));
    }

    /// <summary>
    /// Gets or creates a cached delegate that writes the specified field.
    /// </summary>
    public override Action<object, TProperty> CreateFieldSetter<TProperty>(FieldInfo fieldInfo)
    {
        return GetOrAdd<Action<object, TProperty>>(
            key: (nameof(CreateFieldSetter), typeof(TProperty), fieldInfo),
            valueFactory: key => _accessor.CreateFieldSetter<TProperty>((FieldInfo)key.member!));
    }

    /// <summary>
    /// Gets or creates a cached delegate that builds an immutable dictionary from key-value pairs.
    /// </summary>
    public override Func<IEnumerable<KeyValuePair<TKey, TValue>>, TCollection> CreateImmutableDictionaryCreateRangeDelegate<TCollection, TKey, TValue>()
    {
        return GetOrAdd<Func<IEnumerable<KeyValuePair<TKey, TValue>>, TCollection>>(
            key: (nameof(CreateImmutableDictionaryCreateRangeDelegate), typeof((TCollection, TKey, TValue)), null),
            valueFactory: _ => _accessor.CreateImmutableDictionaryCreateRangeDelegate<TCollection, TKey, TValue>());
    }

    /// <summary>
    /// Gets or creates a cached delegate that builds an immutable collection from a sequence.
    /// </summary>
    public override Func<IEnumerable<TElement>, TCollection> CreateImmutableEnumerableCreateRangeDelegate<TCollection, TElement>()
    {
        return GetOrAdd<Func<IEnumerable<TElement>, TCollection>>(
            key: (nameof(CreateImmutableEnumerableCreateRangeDelegate), typeof((TCollection, TElement)), null),
            valueFactory: _ => _accessor.CreateImmutableEnumerableCreateRangeDelegate<TCollection, TElement>());
    }

    /// <summary>
    /// Gets or creates a cached delegate that invokes the specified constructor with an argument array.
    /// </summary>
    public override Func<object[], T> CreateParameterizedConstructor<T>(ConstructorInfo constructor)
    {
        return GetOrAdd<Func<object[], T>>(
            key: (nameof(CreateParameterizedConstructor), typeof(T), constructor),
            valueFactory: key => _accessor.CreateParameterizedConstructor<T>((ConstructorInfo)key.member!));
    }

    /// <summary>
    /// Gets or creates a cached delegate that invokes the specified constructor with up to four typed arguments.
    /// </summary>
    public override ParameterizedConstructorDelegate<T, TArg0, TArg1, TArg2, TArg3>? CreateParameterizedConstructor<T, TArg0, TArg1, TArg2, TArg3>(ConstructorInfo constructor)
    {
        return GetOrAdd<ParameterizedConstructorDelegate<T, TArg0, TArg1, TArg2, TArg3>?>(
            key: (nameof(CreateParameterizedConstructor), typeof(T), constructor),
            valueFactory: key => _accessor.CreateParameterizedConstructor<T, TArg0, TArg1, TArg2, TArg3>((ConstructorInfo)key.member!));
    }

    /// <summary>
    /// Gets or creates a cached delegate that invokes the parameterless constructor of the specified type.
    /// </summary>
    public override Func<object>? CreateParameterlessConstructor(Type type, ConstructorInfo? constructorInfo)
    {
        return GetOrAdd<Func<object>?>(
            key: (nameof(CreateParameterlessConstructor), type, constructorInfo),
            valueFactory: key => _accessor.CreateParameterlessConstructor(key.declaringType, (ConstructorInfo?)key.member));
    }

    /// <summary>
    /// Gets or creates a cached delegate that reads the specified property.
    /// </summary>
    public override Func<object, TProperty> CreatePropertyGetter<TProperty>(PropertyInfo propertyInfo)
    {
        return GetOrAdd<Func<object, TProperty>>(
            key: (nameof(CreatePropertyGetter), typeof(TProperty), propertyInfo),
            valueFactory: key => _accessor.CreatePropertyGetter<TProperty>((PropertyInfo)key.member!));
    }

    /// <summary>
    /// Gets or creates a cached delegate that writes the specified property.
    /// </summary>
    public override Action<object, TProperty> CreatePropertySetter<TProperty>(PropertyInfo propertyInfo)
    {
        return GetOrAdd<Action<object, TProperty>>(
            key: (nameof(CreatePropertySetter), typeof(TProperty), propertyInfo),
            valueFactory: key => _accessor.CreatePropertySetter<TProperty>((PropertyInfo)key.member!));
    }

    private TValue GetOrAdd<TValue>(CacheKey key, Func<CacheKey, object?> valueFactory) where TValue : class?
    {
        return (TValue)_cache.GetOrAdd(key, valueFactory!);
    }
}

