using System;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

[PropertyEditor(typeof(string))]
public class PropertyStringEditor : PropertyEditor
{
    public PropertyStringEditor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }

    public override Control CreateControl()
    {
        return new Views.PropertyStringEditor()
        {
            DataContext = this,
        };
    }
}