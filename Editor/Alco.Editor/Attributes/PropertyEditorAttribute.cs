using System;

namespace Alco.Editor.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class PropertyEditorAttribute : Attribute
{
    public Type[] SupportedTypes { get; }

    public PropertyEditorAttribute(params Type[] supportedTypes)
    {
        SupportedTypes = supportedTypes;
    }
}
