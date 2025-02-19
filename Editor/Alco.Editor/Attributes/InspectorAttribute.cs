using System;
using System.Collections.Generic;

namespace Alco.Editor.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class InspectorAttribute : Attribute
{
    private readonly HashSet<string> _extensions = new();
    public Type AssetType { get; }

    public InspectorAttribute(Type assetType, params string[] extensions)
    {
        AssetType = assetType;
        _extensions.UnionWith(extensions);
    }

    public bool IsSupported(string extension)
    {
        return _extensions.Contains(extension);
    }
}
