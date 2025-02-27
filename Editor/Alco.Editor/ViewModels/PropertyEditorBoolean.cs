using System;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

[PropertyEditor(typeof(bool))]
public class PropertyEditorBoolean : PropertyEditor
{
    public PropertyEditorBoolean(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }

    public override Control CreateControl()
    {
        return new Views.PropertyEditorBoolean()
        {
            DataContext = this,
        };
    }
}