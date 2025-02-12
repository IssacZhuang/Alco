using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Alco;

/// <summary>
/// Provides dynamic property access for a given type through compiled expressions.
/// </summary>
/// <typeparam name="T">The type to create property accessors for.</typeparam>
public class DynamicAccessor<T>
{
    private readonly FrozenDictionary<string, Func<T, object>> _getters;
    private readonly FrozenDictionary<string, Action<T, object>> _setters;

    /// <summary>
    /// Gets an array of property names available in type T.
    /// </summary>
    public string[] Properties { get; }

    public DynamicAccessor()
    {
        Dictionary<string, Func<T, object>> getters = new();
        Dictionary<string, Action<T, object>> setters = new();
        List<string> propertyNames = new();
        foreach (var property in typeof(T).GetProperties())
        {
            getters[property.Name] = CreateGetter(property.Name);
            setters[property.Name] = CreateSetter(property.Name);
            propertyNames.Add(property.Name);
        }
        _getters = getters.ToFrozenDictionary();
        _setters = setters.ToFrozenDictionary();
        Properties = propertyNames.ToArray();
    }

    /// <summary>
    /// Attempts to get the value of a property by name.
    /// </summary>
    /// <param name="obj">The object to get the property value from.</param>
    /// <param name="propertyName">The name of the property to get.</param>
    /// <param name="value">When successful, contains the property value.</param>
    /// <returns>True if the property was found and retrieved, false otherwise.</returns>
    public bool TryGetValue(T obj, string propertyName, [MaybeNullWhen(false)] out object value)
    {
        if (_getters.TryGetValue(propertyName, out var getter))
        {
            value = getter(obj);
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Attempts to set the value of a property by name.
    /// </summary>
    /// <param name="obj">The object to set the property value on.</param>
    /// <param name="propertyName">The name of the property to set.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>True if the property was found and set, false otherwise.</returns>
    public bool TrySetValue(T obj, string propertyName, object value)
    {
        if (_setters.TryGetValue(propertyName, out var setter))
        {
            setter(obj, value);
            return true;
        }
        return false;
    }

    private static Func<T, object> CreateGetter(string propertyName)
    {
        var parameter = Expression.Parameter(typeof(T), "obj");
        var property = Expression.Property(parameter, propertyName);
        var convert = Expression.Convert(property, typeof(object));
        return Expression.Lambda<Func<T, object>>(convert, parameter).Compile();
    }

    private static Action<T, object> CreateSetter(string propertyName)
    {
        var objParameter = Expression.Parameter(typeof(T), "obj");
        var valueParameter = Expression.Parameter(typeof(object), "value");
        var property = Expression.Property(objParameter, propertyName);
        var convertedValue = Expression.Convert(valueParameter, property.Type);
        var assign = Expression.Assign(property, convertedValue);
        return Expression.Lambda<Action<T, object>>(assign, objParameter, valueParameter).Compile();
    }

}

