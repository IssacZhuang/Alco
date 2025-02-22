using System;

namespace Alco.Editor.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class EditorPageAttribute : Attribute
{
    public int Order { get; }

    public EditorPageAttribute(int order = 0)
    {
        Order = order;
    }
}
