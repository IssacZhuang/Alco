using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// Provides dynamic access to properties and fields of type T through compiled expressions.
/// </summary>
/// <typeparam name="T">The type to provide dynamic access for.</typeparam>
/// <remarks>
/// This class caches compiled expressions for property and field access, providing better performance
/// than direct reflection for repeated access operations. Thread-safe through the use of frozen collections.
/// </remarks>
public static class DynamicAccessor<T>
{
    private static readonly FrozenDictionary<string, Func<T, object>> _getters;
    private static readonly FrozenDictionary<string, Action<T, object>> _setters;
    private static readonly string[] _propertyNames;
    private static readonly string[] _fieldNames;

    public static string[] PropertyNames => _propertyNames;
    public static string[] FieldNames => _fieldNames;

    static DynamicAccessor()
    {
        //getter
        Type type = typeof(T);
        Dictionary<string, Func<T, object>> getters = new Dictionary<string, Func<T, object>>();
        Dictionary<string, Action<T, object>> setters = new Dictionary<string, Action<T, object>>();
        List<string> propertyNames = new List<string>();
        List<string> fieldNames = new List<string>();

        var properties = type.GetProperties();
        var fields = type.GetFields();
        foreach (var field in fields)
        {
            getters[field.Name] = CreateFieldGetter(field.Name);
            setters[field.Name] = CreateFieldSetter(field.Name);
            fieldNames.Add(field.Name);
        }

        foreach (var property in properties)
        {
            getters[property.Name] = CreatePropertyGetter(property.Name);
            setters[property.Name] = CreatePropertySetter(property.Name);
            propertyNames.Add(property.Name);
        }

        _getters = getters.ToFrozenDictionary();
        _setters = setters.ToFrozenDictionary();
        _propertyNames = propertyNames.ToArray();
        _fieldNames = fieldNames.ToArray();
    }

    /// <summary>
    /// Attempts to get the value of a property or field by name.
    /// </summary>
    /// <param name="obj">The object to get the value from.</param>
    /// <param name="name">The name of the property or field.</param>
    /// <param name="value">When this method returns, contains the value of the property or field if found, or null if not found.</param>
    /// <returns>true if the property or field was found; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetValue(T obj, string name, [MaybeNullWhen(false)] out object value)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        if (_getters.TryGetValue(name, out var getter))
        {
            value = getter(obj);
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Attempts to set the value of a property or field by name.
    /// </summary>
    /// <param name="obj">The object to set the value on.</param>
    /// <param name="name">The name of the property or field.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>true if the property or field was found and set; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TrySetValue(T obj, string name, object value)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        if (_setters.TryGetValue(name, out var setter))
        {
            setter(obj, value);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Creates a compiled expression for getting a property value.
    /// </summary>
    /// <param name="propertyName">The name of the property to access.</param>
    /// <returns>A compiled function that gets the property value.</returns>
    private static Func<T, object> CreatePropertyGetter(string propertyName)
    {
        var parameter = Expression.Parameter(typeof(T), "obj");
        var property = Expression.Property(parameter, propertyName);
        var convert = Expression.Convert(property, typeof(object));
        return Expression.Lambda<Func<T, object>>(convert, parameter).Compile();
    }

    /// <summary>
    /// Creates a compiled expression for setting a property value.
    /// </summary>
    /// <param name="propertyName">The name of the property to set.</param>
    /// <returns>A compiled action that sets the property value.</returns>
    private static Action<T, object> CreatePropertySetter(string propertyName)
    {
        var objParameter = Expression.Parameter(typeof(T), "obj");
        var valueParameter = Expression.Parameter(typeof(object), "value");
        var property = Expression.Property(objParameter, propertyName);
        var convertedValue = Expression.Convert(valueParameter, property.Type);
        var assign = Expression.Assign(property, convertedValue);
        return Expression.Lambda<Action<T, object>>(assign, objParameter, valueParameter).Compile();
    }

    /// <summary>
    /// Creates a compiled expression for getting a field value.
    /// </summary>
    /// <param name="fieldName">The name of the field to access.</param>
    /// <returns>A compiled function that gets the field value.</returns>
    private static Func<T, object> CreateFieldGetter(string fieldName)
    {
        var parameter = Expression.Parameter(typeof(T), "obj");
        var field = Expression.Field(parameter, fieldName);
        var convert = Expression.Convert(field, typeof(object));
        return Expression.Lambda<Func<T, object>>(convert, parameter).Compile();
    }

    /// <summary>
    /// Creates a compiled expression for setting a field value.
    /// </summary>
    /// <param name="fieldName">The name of the field to set.</param>
    /// <returns>A compiled action that sets the field value.</returns>
    private static Action<T, object> CreateFieldSetter(string fieldName)
    {
        var objParameter = Expression.Parameter(typeof(T), "obj");
        var valueParameter = Expression.Parameter(typeof(object), "value");
        var field = Expression.Field(objParameter, fieldName);
        var convertedValue = Expression.Convert(valueParameter, field.Type);
        var assign = Expression.Assign(field, convertedValue);
        return Expression.Lambda<Action<T, object>>(assign, objParameter, valueParameter).Compile();
    }
}
