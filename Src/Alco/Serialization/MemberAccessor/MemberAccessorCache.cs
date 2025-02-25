using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using CacheKey = (string id, System.Type declaringType, System.Reflection.MemberInfo? member);

namespace Alco;

public class MemberAccessorCache : MemberAccessor
{
    private readonly ConcurrentLruCache<CacheKey, object> _cache;
    private readonly MemberAccessor _accessor;

    public MemberAccessorCache(MemberAccessor accessor, int capacity = 32)
    {
        _accessor = accessor;
        _cache = new(capacity);
    }

    public override void Clear() => _cache.Clear();

    public override Action<TCollection, object?> CreateAddMethodDelegate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TCollection>()
    {
        return GetOrAdd<Action<TCollection, object?>>(
            key: (nameof(CreateAddMethodDelegate), typeof(TCollection), null),
            valueFactory: _ => _accessor.CreateAddMethodDelegate<TCollection>());
    }

    public override Func<object, TProperty> CreateFieldGetter<TProperty>(FieldInfo fieldInfo)
    {
        return GetOrAdd<Func<object, TProperty>>(
            key: (nameof(CreateFieldGetter), typeof(TProperty), fieldInfo),
            valueFactory: key => _accessor.CreateFieldGetter<TProperty>((FieldInfo)key.member!));
    }

    public override Action<object, TProperty> CreateFieldSetter<TProperty>(FieldInfo fieldInfo)
    {
        return GetOrAdd<Action<object, TProperty>>(
            key: (nameof(CreateFieldSetter), typeof(TProperty), fieldInfo),
            valueFactory: key => _accessor.CreateFieldSetter<TProperty>((FieldInfo)key.member!));
    }

    public override Func<IEnumerable<KeyValuePair<TKey, TValue>>, TCollection> CreateImmutableDictionaryCreateRangeDelegate<TCollection, TKey, TValue>()
    {
        return GetOrAdd<Func<IEnumerable<KeyValuePair<TKey, TValue>>, TCollection>>(
            key: (nameof(CreateImmutableDictionaryCreateRangeDelegate), typeof((TCollection, TKey, TValue)), null),
            valueFactory: _ => _accessor.CreateImmutableDictionaryCreateRangeDelegate<TCollection, TKey, TValue>());
    }

    public override Func<IEnumerable<TElement>, TCollection> CreateImmutableEnumerableCreateRangeDelegate<TCollection, TElement>()
    {
        return GetOrAdd<Func<IEnumerable<TElement>, TCollection>>(
            key: (nameof(CreateImmutableEnumerableCreateRangeDelegate), typeof((TCollection, TElement)), null),
            valueFactory: _ => _accessor.CreateImmutableEnumerableCreateRangeDelegate<TCollection, TElement>());
    }

    public override Func<object[], T> CreateParameterizedConstructor<T>(ConstructorInfo constructor)
    {
        return GetOrAdd<Func<object[], T>>(
            key: (nameof(CreateParameterizedConstructor), typeof(T), constructor),
            valueFactory: key => _accessor.CreateParameterizedConstructor<T>((ConstructorInfo)key.member!));
    }

    public override ParameterizedConstructorDelegate<T, TArg0, TArg1, TArg2, TArg3>? CreateParameterizedConstructor<T, TArg0, TArg1, TArg2, TArg3>(ConstructorInfo constructor)
    {
        return GetOrAdd<ParameterizedConstructorDelegate<T, TArg0, TArg1, TArg2, TArg3>?>(
            key: (nameof(CreateParameterizedConstructor), typeof(T), constructor),
            valueFactory: key => _accessor.CreateParameterizedConstructor<T, TArg0, TArg1, TArg2, TArg3>((ConstructorInfo)key.member!));
    }

    public override Func<object>? CreateParameterlessConstructor(Type type, ConstructorInfo? constructorInfo)
    {
        return GetOrAdd<Func<object>?>(
            key: (nameof(CreateParameterlessConstructor), type, constructorInfo),
            valueFactory: key => _accessor.CreateParameterlessConstructor(key.declaringType, (ConstructorInfo?)key.member));
    }

    public override Func<object, TProperty> CreatePropertyGetter<TProperty>(PropertyInfo propertyInfo)
    {
        return GetOrAdd<Func<object, TProperty>>(
            key: (nameof(CreatePropertyGetter), typeof(TProperty), propertyInfo),
            valueFactory: key => _accessor.CreatePropertyGetter<TProperty>((PropertyInfo)key.member!));
    }

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

