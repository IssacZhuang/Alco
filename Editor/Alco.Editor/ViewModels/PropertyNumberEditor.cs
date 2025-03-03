using System;
using Alco.Editor.Attributes;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Alco.Editor.ViewModels;

[PropertyEditor(
    typeof(int),
    typeof(uint),
    typeof(long),
    typeof(ulong),
    typeof(short),
    typeof(ushort),
    typeof(byte),
    typeof(sbyte),
    typeof(float),
    typeof(double),
    typeof(Half),
    typeof(decimal))]
public class PropertyNumberEditor : PropertyEditor
{
    public PropertyNumberEditor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }


    public override Control CreateControl()
    {
        Control control = new Views.PropertyNumberEditor()
        {
            DataContext = this,
        };
        return control;
    }

    public string GetFormatString()
    {
        return MemberInfo.MemberType switch
        {
            Type t when t == typeof(int) => "F0",
            Type t when t == typeof(uint) => "F0",
            Type t when t == typeof(long) => "F0",
            Type t when t == typeof(ulong) => "F0",
            Type t when t == typeof(short) => "F0",
            Type t when t == typeof(ushort) => "F0",
            Type t when t == typeof(byte) => "F0",
            Type t when t == typeof(sbyte) => "F0",
            Type t when t == typeof(float) => "G",
            Type t when t == typeof(double) => "G",
            Type t when t == typeof(Half) => "G",
            Type t when t == typeof(decimal) => "G",
            _ => "G"
        };
    }

}
