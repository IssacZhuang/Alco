using System;
using System.Collections.Generic;
using System.Linq;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

[PropertyEditor(typeof(Enum))]
public class PropertyEnumEditor : PropertyEditor
{
    private readonly List<string> _enumValues;

    public IEnumerable<string> EnumValues => _enumValues;
    public PropertyEnumEditor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
        if (!memberInfo.MemberType.IsEnum)
        {
            throw new ArgumentException($"Member {memberInfo.Name} is not an enum");
        }
        _enumValues = Enum.GetNames(memberInfo.MemberType).ToList();
    }

    public string SelectedValue
    {
        get => GetValue();
        set
        {
            SetValue(value);
            DoValueChangedEvent();
        }
    }

    private string GetValue()
    {
        if (!MemberInfo.CanRead)
        {
            return string.Empty;
        }

        var value = MemberInfo.GetValue<object>(Target);
        return value?.ToString() ?? string.Empty;
    }

    private void SetValue(string value)
    {
        if (!MemberInfo.CanWrite || string.IsNullOrEmpty(value))
        {
            return;
        }

        var enumValue = Enum.Parse(MemberInfo.MemberType, value);
        MemberInfo.SetValue(Target, enumValue);
    }


    public override Control CreateControl()
    {
        var control = new Views.PropertyEnumEditor
        {
            DataContext = this
        };
        return control;
    }
}

