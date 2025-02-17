using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace Alco;

/// <summary>
/// Provides dynamic access to properties and fields of a type through compiled expressions.
/// </summary>
/// <remarks>
/// This class caches compiled expressions for property and field access, providing better performance
/// than direct reflection for repeated access operations. Thread-safe through the use of frozen collections.
/// </remarks>
public class DynamicAccessor
{
    private struct Getter
    {
        public Getter(Type type, Func<object, object> method)
        {
            Type = type;
            Method = method;
        }
        public Type Type;
        public Func<object, object> Method;
    }

    private struct Setter
    {
        public Setter(Type type, Action<object, object> method)
        {
            Type = type;
            Method = method;
        }
        public Type Type;
        public Action<object, object> Method;
    }

    private readonly FrozenDictionary<string, Getter> _getters;
    private readonly FrozenDictionary<string, Setter> _setters;
    private readonly string[] _propertyNames;
    private readonly string[] _fieldNames;
    private readonly Type _targetType;

    public string[] PropertyNames => _propertyNames;
    public string[] FieldNames => _fieldNames;
    public Type TargetType => _targetType;

    public DynamicAccessor(Type targetType)
    {
        _targetType = targetType;
        Dictionary<string, Getter> getters = new Dictionary<string, Getter>();
        Dictionary<string, Setter> setters = new Dictionary<string, Setter>();
        List<string> propertyNames = new List<string>();
        List<string> fieldNames = new List<string>();

        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var properties = targetType.GetProperties(bindingFlags);
        var fields = targetType.GetFields(bindingFlags);

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
    public bool TryGetValue(object obj, string name, [MaybeNullWhen(false)] out object value)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        if (!_targetType.IsAssignableFrom(obj.GetType()))
            throw new ArgumentException($"Object type {obj.GetType()} is not assignable to {_targetType}");

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
    public object GetValue(object obj, string name)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        if (!_targetType.IsAssignableFrom(obj.GetType()))
            throw new ArgumentException($"Object type {obj.GetType()} is not assignable to {_targetType}");

        if (!_getters.TryGetValue(name, out var getter))
            throw new ArgumentException($"Property or field '{name}' not found on type {_targetType}");
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
    public bool TrySetValue(object obj, string name, object value)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        if (!_targetType.IsAssignableFrom(obj.GetType()))
            throw new ArgumentException($"Object type {obj.GetType()} is not assignable to {_targetType}");

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
    public void SetValue(object obj, string name, object value)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        if (!_targetType.IsAssignableFrom(obj.GetType()))
            throw new ArgumentException($"Object type {obj.GetType()} is not assignable to {_targetType}");

        if (!_setters.TryGetValue(name, out var setter))
        {
            throw new ArgumentException($"Property or field '{name}' not found on type {_targetType}");
        }

        if (!setter.Type.IsAssignableFrom(value.GetType()))
        {
            throw new ArgumentException($"Value type {value.GetType()} is not assignable to setter type {setter.Type}");
        }
        setter.Method(obj, value);
    }

    private Getter CreatePropertyGetter(PropertyInfo property)
    {
        var parameter = Expression.Parameter(typeof(object), "obj");
        var convertedParameter = Expression.Convert(parameter, _targetType);
        var propertyAccess = Expression.Property(convertedParameter, property);
        var convert = Expression.Convert(propertyAccess, typeof(object));
        return new Getter(property.PropertyType, Expression.Lambda<Func<object, object>>(convert, parameter).Compile());
    }

    private Setter CreatePropertySetter(PropertyInfo property)
    {
        var objParameter = Expression.Parameter(typeof(object), "obj");
        var valueParameter = Expression.Parameter(typeof(object), "value");
        var convertedObjParameter = Expression.Convert(objParameter, _targetType);
        var propertyAccess = Expression.Property(convertedObjParameter, property);
        var convertedValue = Expression.Convert(valueParameter, property.PropertyType);
        var assign = Expression.Assign(propertyAccess, convertedValue);
        return new Setter(property.PropertyType, Expression.Lambda<Action<object, object>>(assign, objParameter, valueParameter).Compile());
    }

    private Getter CreateFieldGetter(FieldInfo field)
    {
        var parameter = Expression.Parameter(typeof(object), "obj");
        var convertedParameter = Expression.Convert(parameter, _targetType);
        var fieldAccess = Expression.Field(convertedParameter, field);
        var convert = Expression.Convert(fieldAccess, typeof(object));
        return new Getter(field.FieldType, Expression.Lambda<Func<object, object>>(convert, parameter).Compile());
    }

    private Setter CreateFieldSetter(FieldInfo field)
    {
        var objParameter = Expression.Parameter(typeof(object), "obj");
        var valueParameter = Expression.Parameter(typeof(object), "value");
        var convertedObjParameter = Expression.Convert(objParameter, _targetType);
        var fieldAccess = Expression.Field(convertedObjParameter, field);
        var convertedValue = Expression.Convert(valueParameter, field.FieldType);
        var assign = Expression.Assign(fieldAccess, convertedValue);
        return new Setter(field.FieldType, Expression.Lambda<Action<object, object>>(assign, objParameter, valueParameter).Compile());
    }
}
