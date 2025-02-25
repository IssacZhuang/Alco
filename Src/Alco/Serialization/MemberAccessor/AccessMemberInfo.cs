using System;
using System.Globalization;
using System.Reflection;

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
    public Type PropertyType { get; }

    /// <summary>
    /// Gets the name of the member.
    /// </summary>
    public string Name { get; }

    public bool CanRead { get; }
    public bool CanWrite { get; }

    /// <summary>
    /// Gets a function that retrieves the value of this member from an object.
    /// </summary>
    protected abstract object? MethodGet { get; }

    /// <summary>
    /// Gets an action that sets the value of this member on an object.
    /// </summary>
    protected abstract object? MethodSet { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessMemberInfo"/> class.
    /// </summary>
    /// <param name="propertyType">The type of the property or field.</param>
    /// <param name="name">The name of the member.</param>
    internal AccessMemberInfo(bool canRead, bool canWrite, Type propertyType, string name)
    {
        PropertyType = propertyType;
        Name = name;
        CanRead = canRead;
        CanWrite = canWrite;
    }

    /// <summary>
    /// Gets the value of the member as the specified type. 
    /// </summary>
    /// <param name="obj">The object to get the value from.</param>
    /// <typeparam name="T">The type to get the value as.</typeparam>
    /// <returns>The value of the member as the specified type, or null if the value is not of the specified type.</returns>
    public T? GetValue<T>(object obj)
    {
        if (MethodGet is Func<object, T> getter)
        {
            return getter(obj);
        }
        throw new InvalidCastException($"Cannot get value of {Name} as {typeof(T)}");
    }

    /// <summary>
    /// Sets the value of the member to the specified value.
    /// </summary>
    /// <param name="obj">The object to set the value on.</param>
    /// <param name="value">The value to set the member to.</param>
    public void SetValue<T>(object obj, T value)
    {
        if (MethodSet is Action<object, T> setter)
        {
            setter(obj, value);
        }
    }

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


internal sealed class AccessPropertyInfo<T> : AccessMemberInfo
{

    protected override object? MethodGet { get; }
    protected override object? MethodSet { get; }

    public AccessPropertyInfo(PropertyInfo propertyInfo, MemberAccessor accessor) :
    base(propertyInfo.GetMethod?.IsPublic == true, propertyInfo.SetMethod?.IsPublic == true, propertyInfo.PropertyType, propertyInfo.Name)
    {
        if (CanRead)
        {
            MethodGet = accessor.CreatePropertyGetter<T>(propertyInfo);
        }

        if (CanWrite)
        {
            MethodSet = accessor.CreatePropertySetter<T>(propertyInfo);
        }
    }
}

/// <summary>
/// Provides access to a field with strongly-typed getters and setters.
/// </summary>
/// <typeparam name="T">The type of the field.</typeparam>
internal sealed class AccessFieldInfo<T> : AccessMemberInfo
{
    protected override object? MethodGet { get; }
    protected override object? MethodSet { get; }

    public AccessFieldInfo(FieldInfo fieldInfo, MemberAccessor accessor) :
    base(true, true, fieldInfo.FieldType, fieldInfo.Name)
    {
        MethodGet = accessor.CreateFieldGetter<T>(fieldInfo);
        MethodSet = accessor.CreateFieldSetter<T>(fieldInfo);
    }
}

