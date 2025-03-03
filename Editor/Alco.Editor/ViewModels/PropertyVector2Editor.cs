using System;
using System.Drawing;
using System.Numerics;
using Alco.Editor.Attributes;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Alco.Editor.ViewModels;

[PropertyEditor(
    typeof(Vector2),
    typeof(int2),
    typeof(uint2),
    typeof(Half2))]
public class PropertyVector2Editor : PropertyEditor
{
    public PropertyVector2Editor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }

    public decimal X
    {
        get => GetValueX();
        set => SetValueX(value);
    }

    public decimal Y
    {
        get => GetValueY();
        set => SetValueY(value);
    }

    public override Control CreateControl()
    {
        Control control = new Views.PropertyVector2Editor()
        {
            DataContext = this,
        };
        return control;
    }

    public string GetFormatString()
    {
        return PropertyType switch
        {
            Type t when t == typeof(Vector2) => "G",
            Type t when t == typeof(int2) => "F0",
            Type t when t == typeof(uint2) => "F0",
            Type t when t == typeof(Half2) => "G",
            _ => "G"
        };
    }

    private T GetVector<T>() where T : struct
    {
        return MemberInfo.GetValue<T>(Target);
    }

    private void SetVector<T>(T value) where T : struct
    {
        MemberInfo.SetValue(Target, value);
    }

    private decimal GetValueX()
    {
        return PropertyType switch
        {
            Type t when t == typeof(Vector2) => (decimal)GetVector<Vector2>().X,
            Type t when t == typeof(int2) => (decimal)GetVector<int2>().X,
            Type t when t == typeof(uint2) => (decimal)GetVector<uint2>().X,
            Type t when t == typeof(Half2) => (decimal)GetVector<Half2>().X,
            _ => throw new InvalidOperationException($"Unsupported type: {PropertyType}")
        };
    }

    private decimal GetValueY()
    {
        return PropertyType switch
        {
            Type t when t == typeof(Vector2) => (decimal)GetVector<Vector2>().Y,
            Type t when t == typeof(int2) => (decimal)GetVector<int2>().Y,
            Type t when t == typeof(uint2) => (decimal)GetVector<uint2>().Y,
            Type t when t == typeof(Half2) => (decimal)GetVector<Half2>().Y,
            _ => throw new InvalidOperationException($"Unsupported type: {PropertyType}")
        };
    }

    private void SetValueX(decimal value)
    {
        switch (PropertyType)
        {
            case Type t when t == typeof(Vector2):
                SetVector(new Vector2((float)value, GetVector<Vector2>().Y));
                break;
            case Type t when t == typeof(int2):
                SetVector(new int2((int)value, GetVector<int2>().Y));
                break;
            case Type t when t == typeof(uint2):
                SetVector(new uint2((uint)value, GetVector<uint2>().Y));
                break;
            case Type t when t == typeof(Half2):
                SetVector(new Half2((Half)value, GetVector<Half2>().Y));
                break;
            default:
                throw new InvalidOperationException($"Unsupported type: {PropertyType}");
        }
    }

    private void SetValueY(decimal value)
    {
        switch (PropertyType)
        {
            case Type t when t == typeof(Vector2):
                SetVector(new Vector2(GetVector<Vector2>().X, (float)value));
                break;
            case Type t when t == typeof(int2):
                SetVector(new int2(GetVector<int2>().X, (int)value));
                break;
            case Type t when t == typeof(uint2):
                SetVector(new uint2(GetVector<uint2>().X, (uint)value));
                break;
            case Type t when t == typeof(Half2):
                SetVector(new Half2(GetVector<Half2>().X, (Half)value));
                break;
            default:
                throw new InvalidOperationException($"Unsupported type: {PropertyType}");
        }
    }
}
