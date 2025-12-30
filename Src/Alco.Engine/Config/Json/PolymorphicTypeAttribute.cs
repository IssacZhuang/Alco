using System;

namespace Alco.Engine;

/// <summary>
/// Marks a root type whose derived types should be included for polymorphic JSON serialization.
/// Apply this attribute to a base class to enable automatic discovery of all concrete subclasses.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PolymorphicTypeAttribute : Attribute
{
}


