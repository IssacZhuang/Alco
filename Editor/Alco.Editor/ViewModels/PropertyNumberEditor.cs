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
    public decimal Value
    {
        get => GetValue();
        set
        {
            SetValue(value);
            Refresh();
        }
    }
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

    private decimal GetValue()
    {
        return MemberInfo.MemberType switch
        {
            Type t when t == typeof(int) => MemberInfo.GetValue<int>(Target)!,
            Type t when t == typeof(uint) => MemberInfo.GetValue<uint>(Target)!,
            Type t when t == typeof(long) => MemberInfo.GetValue<long>(Target)!,
            Type t when t == typeof(ulong) => MemberInfo.GetValue<ulong>(Target)!,
            Type t when t == typeof(short) => MemberInfo.GetValue<short>(Target)!,
            Type t when t == typeof(ushort) => MemberInfo.GetValue<ushort>(Target)!,
            Type t when t == typeof(byte) => MemberInfo.GetValue<byte>(Target)!,
            Type t when t == typeof(sbyte) => MemberInfo.GetValue<sbyte>(Target)!,
            Type t when t == typeof(float) => (decimal)MemberInfo.GetValue<float>(Target)!,
            Type t when t == typeof(double) => (decimal)MemberInfo.GetValue<double>(Target)!,
            Type t when t == typeof(Half) => (decimal)MemberInfo.GetValue<Half>(Target)!,
            Type t when t == typeof(decimal) => MemberInfo.GetValue<decimal>(Target)!,
            _ => throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}"),
        };
    }

    private void SetValue(decimal value)
    {
        switch (MemberInfo.MemberType)
        {
            case Type t when t == typeof(int):
                MemberInfo.SetValue(Target, (int)value);
                break;
            case Type t when t == typeof(uint):
                MemberInfo.SetValue(Target, (uint)value);
                break;
            case Type t when t == typeof(long):
                MemberInfo.SetValue(Target, (long)value);
                break;
            case Type t when t == typeof(ulong):
                MemberInfo.SetValue(Target, (ulong)value);
                break;
            case Type t when t == typeof(short):
                MemberInfo.SetValue(Target, (short)value);
                break;
            case Type t when t == typeof(ushort):
                MemberInfo.SetValue(Target, (ushort)value);
                break;
            case Type t when t == typeof(byte):
                MemberInfo.SetValue(Target, (byte)value);
                break;
            case Type t when t == typeof(sbyte):
                MemberInfo.SetValue(Target, (sbyte)value);
                break;
            case Type t when t == typeof(float):
                MemberInfo.SetValue(Target, (float)value);
                break;
            case Type t when t == typeof(double):
                MemberInfo.SetValue(Target, (double)value);
                break;
            case Type t when t == typeof(Half):
                MemberInfo.SetValue(Target, (Half)value);   
                break;
            case Type t when t == typeof(decimal):
                MemberInfo.SetValue(Target, value);
                break;
            default:
                throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}");
        }
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
