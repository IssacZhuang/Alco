using System;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

[PropertyEditor(typeof(string))]
public class PropertyEditorString : PropertyEditor
{
    public PropertyEditorString(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }

    public override Control CreateControl()
    {
        return new Views.PropertyEditorString()
        {
            DataContext = this,
        };
    }
}