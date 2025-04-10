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

    public string Value
    {
        get => MemberInfo.GetValue<string>(Target)!;
        set
        {
            MemberInfo.SetValue(Target, value);
            DoValueChangedEvent();
        }
    }

    public override Control CreateControl()
    {
        return new Views.PropertyStringEditor()
        {
            DataContext = this,
        };
    }

    public override void SetDefaultValue()
    {
        Value = string.Empty;
    }
}