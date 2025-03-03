using System;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class PropertyListEditor : PropertyEditor
{
    public PropertyListEditor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }

    public override Control CreateControl()
    {
        return new Views.PropertyListEditor()
        {
            DataContext = this,
        };
    }
}
