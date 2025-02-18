using System;

namespace Alco.Editor.Attributes;


[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ContextMenuItemAttribute : Attribute
{
    public string Path { get; }

    public ContextMenuItemAttribute(string path)
    {
        Path = path;
    }
}



