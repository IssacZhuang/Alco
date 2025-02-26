using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// Base class for accessing members (properties and fields) of objects through reflection.
/// Provides a unified interface for getting and setting values regardless of member type.
/// </summary>
public abstract class AccessMemberInfo
{
    /// <summary>
    /// Gets the type of the property or field.
    /// </summary>
    public Type MemberType { get; }

    /// <summary>
    /// Gets the name of the member.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the member can be read.
    /// </summary>
    public bool CanRead { get; }

    /// <summary>
    /// Gets a value indicating whether the member can be written to.
    /// </summary>
    public bool CanWrite { get; }


    /// <summary>
    /// Initializes a new instance of the <see cref="AccessMemberInfo"/> class.
    /// </summary>
    /// <param name="propertyType">The type of the property or field.</param>
    /// <param name="name">The name of the member.</param>
    internal AccessMemberInfo(bool canRead, bool canWrite, Type propertyType, string name)
    {
        MemberType = propertyType;
        Name = name;
        CanRead = canRead;
        CanWrite = canWrite;
    }

    /// <summary>
    /// Gets the value of the member as the specified type. 
    /// The type must be the same as the type of the member.
    /// </summary>
    /// <param name="obj">The object to get the value from.</param>
    /// <typeparam name="TTarget">The type to get the value as.</typeparam>
    /// <returns>The value of the member as the specified type, or null if the value is not of the specified type.</returns>
    public abstract TTarget? GetValue<TTarget>(object obj);

    /// <summary>
    /// Sets the value of the member to the specified value.
    /// The type must be the same as the type of the member.
    /// </summary>
    /// <param name="obj">The object to set the value on.</param>
    /// <param name="value">The value to set the member to.</param>
    public abstract void SetValue<T>(object obj, T value);



    /// <summary>
    /// Creates an <see cref="AccessMemberInfo"/> instance for a property.
    /// </summary>
    /// <param name="propertyInfo">The property information.</param>
    /// <param name="accessor">The member accessor to use for creating getters and setters.</param>
    /// <returns>An <see cref="AccessMemberInfo"/> instance for the property.</returns>
    public static AccessMemberInfo Create(PropertyInfo propertyInfo, MemberAccessor accessor)
    {
        return (AccessMemberInfo)typeof(AccessPropertyInfo<>).
            MakeGenericType(propertyInfo.PropertyType).
            CreateInstanceNoWrapExceptions([typeof(PropertyInfo), typeof(MemberAccessor)], [propertyInfo, accessor])!;
    }

    /// <summary>
    /// Creates an <see cref="AccessMemberInfo"/> instance for a field.
    /// </summary>
    /// <param name="fieldInfo">The field information.</param>
    /// <param name="accessor">The member accessor to use for creating getters and setters.</param>
    /// <returns>An <see cref="AccessMemberInfo"/> instance for the field.</returns>
    public static AccessMemberInfo Create(FieldInfo fieldInfo, MemberAccessor accessor)
    {
        return (AccessMemberInfo)typeof(AccessFieldInfo<>).
            MakeGenericType(fieldInfo.FieldType).
            CreateInstanceNoWrapExceptions([typeof(FieldInfo), typeof(MemberAccessor)], [fieldInfo, accessor])!;
    }

}

internal abstract class AccessMemberInfo<T> : AccessMemberInfo
{
    protected Func<object, T>? _typedGet;
    protected Action<object, T>? _typedSet;

    internal AccessMemberInfo(bool canRead, bool canWrite, Type propertyType, string name) :
    base(canRead, canWrite, propertyType, name)
    {

    }

    public override TTarget GetValue<TTarget>(object obj)
    {
        if (_typedGet is null)
        {
            throw new InvalidOperationException($"Property {Name} is not readable.");
        }

        T value = _typedGet(obj);

        if (value is null)
        {
            return default!;
        }
        else if (value is TTarget targetValue)
        {
            return targetValue;
        }

        throw new InvalidCastException($"Cannot convert value of type {typeof(T)} to {typeof(TTarget)}");
    }

    public override void SetValue<TTarget>(object obj, TTarget value)
    {
        if (_typedSet is null)
        {
            throw new InvalidOperationException($"Property {Name} is not writable.");
        }

        if (value is T typedValue)
        {
            _typedSet(obj, typedValue);
        }
        else
        {
            throw new InvalidCastException($"Cannot convert value of type {typeof(TTarget)} to {typeof(T)}");
        }
    }
}


internal sealed class AccessPropertyInfo<T> : AccessMemberInfo<T>
{

    public AccessPropertyInfo(PropertyInfo propertyInfo, MemberAccessor accessor) :
    base(propertyInfo.GetMethod?.IsPublic == true, propertyInfo.SetMethod?.IsPublic == true, propertyInfo.PropertyType, propertyInfo.Name)
    {
        if (CanRead)
        {
            Func<object, T> typedGetter = accessor.CreatePropertyGetter<T>(propertyInfo);
            _typedGet = typedGetter;
        }

        if (CanWrite)
        {
            Action<object, T> typedSetter = accessor.CreatePropertySetter<T>(propertyInfo);
            _typedSet = typedSetter;
        }
    }

}

/// <summary>
/// Provides access to a field with strongly-typed getters and setters.
/// </summary>
/// <typeparam name="T">The type of the field.</typeparam>
internal sealed class AccessFieldInfo<T> : AccessMemberInfo<T>
{
    public AccessFieldInfo(FieldInfo fieldInfo, MemberAccessor accessor) :
    base(true, true, fieldInfo.FieldType, fieldInfo.Name)
    {
        Func<object, T> typedGetter = accessor.CreateFieldGetter<T>(fieldInfo);
        _typedGet = typedGetter;

        Action<object, T> typedSetter = accessor.CreateFieldSetter<T>(fieldInfo);
        _typedSet = typedSetter;
    }
}

