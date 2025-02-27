using System;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

[PropertyEditor(typeof(bool))]
public class PropertyBooleanEditor : PropertyEditor
{
    public PropertyBooleanEditor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }

    public override Control CreateControl()
    {
        return new Views.PropertyBooleanEditor()
        {
            DataContext = this,
        };
    }
}