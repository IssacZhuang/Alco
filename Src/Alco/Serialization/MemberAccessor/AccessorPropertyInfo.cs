using System;
using System.Globalization;
using System.Reflection;

namespace Alco;

public abstract class AccessorPropertyInfo
{
    public bool CanRead { get; }
    public bool CanWrite { get; }
    public Type PropertyType { get; }
    public string Name { get; }
    public abstract Func<object, object?>? Getter { get; }
    public abstract Action<object, object?>? Setter { get; }

    public AccessorPropertyInfo(PropertyInfo propertyInfo)
    {
        CanRead = propertyInfo.CanRead;
        CanWrite = propertyInfo.CanWrite;
        PropertyType = propertyInfo.PropertyType;
        Name = propertyInfo.Name;
    }

    public static AccessorPropertyInfo Create(PropertyInfo propertyInfo, MemberAccessor accessor)
    {
        return (AccessorPropertyInfo)typeof(AccessorPropertyInfo<>).
            MakeGenericType(propertyInfo.PropertyType).
            CreateInstanceNoWrapExceptions([typeof(PropertyInfo), typeof(MemberAccessor)], [propertyInfo, accessor])!;
    }
}

public sealed class AccessorPropertyInfo<T> : AccessorPropertyInfo
{
    public override Func<object, object?>? Getter { get; }
    public override Action<object, object?>? Setter { get; }

    public Func<object, T>? GetterTyped { get; }
    public Action<object, T>? SetterTyped { get; }

    public AccessorPropertyInfo(PropertyInfo propertyInfo, MemberAccessor accessor) : base(propertyInfo)
    {

        if (CanRead)
        {
            var typedGetter = accessor.CreatePropertyGetter<T>(propertyInfo);
            Getter = typedGetter is Func<object, object?> getter ? getter : obj => typedGetter(obj);
            GetterTyped = typedGetter;
        }

        if (CanWrite)
        {
            var typedSetter = accessor.CreatePropertySetter<T>(propertyInfo);
            Setter = typedSetter is Action<object, object?> setter ? setter : (obj, value) => typedSetter(obj, (T)value!);
            SetterTyped = typedSetter;
        }
    }

}

