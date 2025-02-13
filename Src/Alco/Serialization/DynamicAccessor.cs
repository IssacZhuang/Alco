using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Reflection;

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
    private struct Getter
    {
        public Getter(Type type, Func<T, object> method)
        {
            Type = type;
            Method = method;
        }
        public Type Type;
        public Func<T, object> Method;
    }

    private struct Setter
    {
        public Setter(Type type, Action<T, object> method)
        {
            Type = type;
            Method = method;
        }
        public Type Type;
        public Action<T, object> Method;
    }

    private static readonly FrozenDictionary<string, Getter> _getters;
    private static readonly FrozenDictionary<string, Setter> _setters;
    private static readonly string[] _propertyNames;
    private static readonly string[] _fieldNames;

    public static string[] PropertyNames => _propertyNames;
    public static string[] FieldNames => _fieldNames;

    static DynamicAccessor()
    {
        Type type = typeof(T);
        Dictionary<string, Getter> getters = new Dictionary<string, Getter>();
        Dictionary<string, Setter> setters = new Dictionary<string, Setter>();
        List<string> propertyNames = new List<string>();
        List<string> fieldNames = new List<string>();

        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var properties = type.GetProperties(bindingFlags);
        var fields = type.GetFields(bindingFlags);

        foreach (var field in fields)
        {
            getters[field.Name] = CreateFieldGetter(field);
            setters[field.Name] = CreateFieldSetter(field);
            fieldNames.Add(field.Name);
        }

        foreach (var property in properties)
        {
            getters[property.Name] = CreatePropertyGetter(property);
            setters[property.Name] = CreatePropertySetter(property);
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
        if (_getters.TryGetValue(name, out var getter))
        {
            value = getter.Method(obj);
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Gets the value of a property or field by name.
    /// </summary>
    /// <param name="obj">The object to get the value from.</param>
    /// <param name="name">The name of the property or field.</param>
    /// <returns>The value of the property or field.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object GetValue(T obj, string name)
    {
        if (!_getters.TryGetValue(name, out var getter))
            throw new ArgumentException($"Property or field '{name}' not found on type {typeof(T)}");
        return getter.Method(obj);
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
        if (!_setters.TryGetValue(name, out var setter))
        {
            return false;
        }

        if (setter.Type.IsAssignableFrom(value.GetType()))
        {
            setter.Method(obj, value);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Sets the value of a property or field by name.
    /// </summary>
    /// <param name="obj">The object to set the value on.</param>
    /// <param name="name">The name of the property or field.</param>
    /// <param name="value">The value to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetValue(T obj, string name, object value)
    {
        if (!_setters.TryGetValue(name, out var setter))
        {
            throw new ArgumentException($"Property or field '{name}' not found on type {typeof(T)}");
        }

        if (!setter.Type.IsAssignableFrom(value.GetType()))
        {
            throw new ArgumentException($"Value type {value.GetType()} is not assignable to setter type {setter.Type}");
        }
        setter.Method(obj, value);
    }

    private static Getter CreatePropertyGetter(PropertyInfo property)
    {
        var parameter = Expression.Parameter(typeof(T), "obj");
        var propertyAccess = Expression.Property(parameter, property);
        var convert = Expression.Convert(propertyAccess, typeof(object));
        return new Getter(property.PropertyType, Expression.Lambda<Func<T, object>>(convert, parameter).Compile());
    }

    private static Setter CreatePropertySetter(PropertyInfo property)
    {
        var objParameter = Expression.Parameter(typeof(T), "obj");
        var valueParameter = Expression.Parameter(typeof(object), "value");
        var propertyAccess = Expression.Property(objParameter, property);
        var convertedValue = Expression.Convert(valueParameter, property.PropertyType);
        var assign = Expression.Assign(propertyAccess, convertedValue);
        return new Setter(property.PropertyType, Expression.Lambda<Action<T, object>>(assign, objParameter, valueParameter).Compile());
    }

    private static Getter CreateFieldGetter(FieldInfo field)
    {
        var parameter = Expression.Parameter(typeof(T), "obj");
        var fieldAccess = Expression.Field(parameter, field);
        var convert = Expression.Convert(fieldAccess, typeof(object));
        return new Getter(field.FieldType, Expression.Lambda<Func<T, object>>(convert, parameter).Compile());
    }

    private static Setter CreateFieldSetter(FieldInfo field)
    {
        var objParameter = Expression.Parameter(typeof(T), "obj");
        var valueParameter = Expression.Parameter(typeof(object), "value");
        var fieldAccess = Expression.Field(objParameter, field);
        var convertedValue = Expression.Convert(valueParameter, field.FieldType);
        var assign = Expression.Assign(fieldAccess, convertedValue);
        return new Setter(field.FieldType, Expression.Lambda<Action<T, object>>(assign, objParameter, valueParameter).Compile());
    }
}
