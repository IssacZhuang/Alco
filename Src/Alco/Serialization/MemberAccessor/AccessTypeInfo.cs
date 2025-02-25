using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Alco;

/// <summary>
/// Provides type information for accessing public properties and fields of a type.
/// This class is used for reflection-based member access and serialization.
/// </summary>
public class AccessTypeInfo
{
    /// <summary>
    /// Binding flags used to retrieve all instance members of a type,
    /// including both public and non-public members that are declared in the type itself.
    /// </summary>
    private const BindingFlags AllInstanceMembers =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

    private readonly Func<object>? _constructor;

    /// <summary>
    /// Gets an array of <see cref="AccessMemberInfo"/> objects representing 
    /// all accessible members (public properties and fields) of the type.
    /// </summary>
    public AccessMemberInfo[] Members { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessTypeInfo"/> class.
    /// </summary>
    /// <param name="type">The type to analyze for accessible members.</param>
    /// <param name="memberAccessor">The member accessor used to create access information for each member.</param>
    public AccessTypeInfo(Type type, MemberAccessor memberAccessor)
    {
        //get all public properties and fields
        var fields = type.GetFields(AllInstanceMembers);
        var properties = type.GetProperties(AllInstanceMembers);
        List<AccessMemberInfo> accessMembers = new();

        foreach (var property in properties)
        {
            if (property.GetMethod?.IsPublic == true ||
                property.SetMethod?.IsPublic == true)
            {
                accessMembers.Add(AccessMemberInfo.Create(property, memberAccessor));
            }
        }

        foreach (var field in fields)
        {
            if (field.IsPublic)
            {
                accessMembers.Add(AccessMemberInfo.Create(field, memberAccessor));
            }
        }

        Members = accessMembers.ToArray();

        // Get the parameterless constructor if available
        var constructorInfo = type.GetConstructor(Type.EmptyTypes);
        _constructor = memberAccessor.CreateParameterlessConstructor(type, constructorInfo);
    }

    /// <summary>
    /// Creates an instance of the type.
    /// </summary>
    /// <typeparam name="T">The type of the instance to create.</typeparam>
    /// <returns>An instance of the type.</returns>
    public T CreateInstance<T>()
    {
        if (_constructor is null)
        {
            throw new InvalidOperationException("Type does not have a parameterless constructor.");
        }

        if (_constructor is Func<T> constructor)
        {
            return constructor();
        }

        return (T)_constructor();
    }
}

