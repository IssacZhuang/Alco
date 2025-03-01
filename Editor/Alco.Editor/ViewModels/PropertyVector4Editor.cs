using System;
using System.Drawing;
using System.Numerics;
using Alco.Editor.Attributes;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Alco.Editor.ViewModels;

[PropertyEditor(
    typeof(Vector4),
    typeof(int4),
    typeof(uint4),
    typeof(Half4))]
public class PropertyVector4Editor : PropertyVector3Editor
{
    public PropertyVector4Editor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }

    public decimal W
    {
        get => GetValueW();
        set => SetValueW(value);
    }

    public override Control CreateControl()
    {
        Control control = new Views.PropertyVector4Editor()
        {
            DataContext = this,
        };
        return control;
    }

    private decimal GetValueW()
    {
        return MemberInfo.MemberType switch
        {
            Type t when t == typeof(Vector4) => (decimal)GetVector<Vector4>().W,
            Type t when t == typeof(int4) => (decimal)GetVector<int4>().W,
            Type t when t == typeof(uint4) => (decimal)GetVector<uint4>().W,
            Type t when t == typeof(Half4) => (decimal)GetVector<Half4>().W,
            _ => throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}")
        };
    }

    private void SetValueW(decimal value)
    {
        switch (MemberInfo.MemberType)
        {
            case Type t when t == typeof(Vector4):
                Vector4 v = GetVector<Vector4>();
                SetVector(new Vector4(v.X, v.Y, v.Z, (float)value));
                break;
            case Type t when t == typeof(int4):
                int4 i = GetVector<int4>();
                SetVector(new int4(i.X, i.Y, i.Z, (int)value));
                break;
            case Type t when t == typeof(uint4):
                uint4 u = GetVector<uint4>();
                SetVector(new uint4(u.X, u.Y, u.Z, (uint)value));
                break;
            case Type t when t == typeof(Half4):
                Half4 h = GetVector<Half4>();
                SetVector(new Half4(h.X, h.Y, h.Z, (Half)value));
                break;
            default:
                throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}");
        }
    }
}