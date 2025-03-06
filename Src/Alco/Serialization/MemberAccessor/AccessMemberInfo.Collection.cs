using System;
using System.Collections;
using System.Collections.Generic;

namespace Alco;

/// <summary>
/// Provides access to a specific item in a list collection.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public class AccessListItemInfo<T> : AccessMemberInfo
{
    private readonly IList<T> _list;
    public int Index { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessListItemInfo{T}"/> class.
    /// </summary>
    /// <param name="list">The list containing the item to access.</param>
    /// <param name="index">The zero-based index of the item in the list.</param>
    public AccessListItemInfo(IList<T> list, int index) :
    base(true, true, typeof(T), $"[{index}]")
    {
        _list = list;
        Index = index;
    }

    /// <summary>
    /// Gets the value of the list item at the specified index.
    /// </summary>
    /// <typeparam name="TTarget">The target type to convert the value to.</typeparam>
    /// <param name="obj">The object instance (not used in this implementation).</param>
    /// <returns>The value of the list item converted to the target type.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to the target type.</exception>
    public override TTarget? GetValue<TTarget>(object obj) where TTarget : default
    {
        if (_list[Index] is TTarget targetValue)
        {
            return targetValue;
        }

        throw new InvalidCastException($"Cannot convert value of type {typeof(T)} to {typeof(TTarget)}");
    }

    /// <summary>
    /// Sets the value of the list item at the specified index.
    /// </summary>
    /// <typeparam name="T1">The type of the value to set.</typeparam>
    /// <param name="obj">The object instance (not used in this implementation).</param>
    /// <param name="value">The value to set.</param>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to the list item type.</exception>
    public override void SetValue<T1>(object obj, T1 value)
    {
        if (value is T typedValue)
        {
            _list[Index] = typedValue;
        }
        else
        {
            throw new InvalidCastException($"Cannot convert value of type {typeof(T1)} to {typeof(T)}");
        }
    }
}

/// <summary>
/// Provides access to a specific item in a dictionary collection.
/// </summary>
/// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
/// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
public class AccessDictionaryItemInfo<TKey, TValue> : AccessMemberInfo
{
    private readonly IDictionary<TKey, TValue> _dictionary;
    private readonly TKey _key;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessDictionaryItemInfo{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="dictionary">The dictionary containing the item to access.</param>
    /// <param name="key">The key of the item in the dictionary.</param>
    public AccessDictionaryItemInfo(IDictionary<TKey, TValue> dictionary, TKey key) :
    base(true, true, typeof(TValue), $"[{key}]")
    {
        _dictionary = dictionary;
        _key = key;
    }

    /// <summary>
    /// Gets the value associated with the specified key in the dictionary.
    /// </summary>
    /// <typeparam name="TTarget">The target type to convert the value to.</typeparam>
    /// <param name="obj">The object instance (not used in this implementation).</param>
    /// <returns>The value associated with the key converted to the target type.</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to the target type.</exception>
    public override TTarget? GetValue<TTarget>(object obj) where TTarget : default
    {
        if (_dictionary[_key] is TTarget targetValue)
        {
            return targetValue;
        }

        throw new InvalidCastException($"Cannot convert value of type {typeof(TValue)} to {typeof(TTarget)}");
    }

    /// <summary>
    /// Sets the value associated with the specified key in the dictionary.
    /// </summary>
    /// <typeparam name="T1">The type of the value to set.</typeparam>
    /// <param name="obj">The object instance (not used in this implementation).</param>
    /// <param name="value">The value to set.</param>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be converted to the dictionary value type.</exception>
    public override void SetValue<T1>(object obj, T1 value)
    {
        if (value is TValue typedValue)
        {
            _dictionary[_key] = typedValue;
        }
        else
        {
            throw new InvalidCastException($"Cannot convert value of type {typeof(T1)} to {typeof(TValue)}");
        }
    }
}


