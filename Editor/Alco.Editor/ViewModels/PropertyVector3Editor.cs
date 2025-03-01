using System;
using System.Drawing;
using System.Numerics;
using Alco.Editor.Attributes;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Alco.Editor.ViewModels;

[PropertyEditor(
    typeof(Vector3),
    typeof(int3),
    typeof(uint3),
    typeof(Half3))]
public class PropertyVector3Editor : PropertyVector2Editor
{
    public PropertyVector3Editor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }

    public decimal Z
    {
        get => GetValueZ();
        set => SetValueZ(value);
    }

    public override Control CreateControl()
    {
        Control control = new Views.PropertyVector3Editor()
        {
            DataContext = this,
        };
        return control;
    }

    private decimal GetValueZ()
    {
        return MemberInfo.MemberType switch
        {
            Type t when t == typeof(Vector3) => (decimal)GetVector<Vector3>().Z,
            Type t when t == typeof(int3) => (decimal)GetVector<int3>().Z,
            Type t when t == typeof(uint3) => (decimal)GetVector<uint3>().Z,
            Type t when t == typeof(Half3) => (decimal)GetVector<Half3>().Z,
            _ => throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}")
        };
    }

    private void SetValueZ(decimal value)
    {
        switch (MemberInfo.MemberType)
        {
            case Type t when t == typeof(Vector3):
                Vector3 v = GetVector<Vector3>();
                SetVector(new Vector3(v.X, v.Y, (float)value));
                break;
            case Type t when t == typeof(int3):
                int3 i = GetVector<int3>();
                SetVector(new int3(i.X, i.Y, (int)value));
                break;
            case Type t when t == typeof(uint3):
                uint3 u = GetVector<uint3>();
                SetVector(new uint3(u.X, u.Y, (uint)value));
                break;
            case Type t when t == typeof(Half3):
                Half3 h = GetVector<Half3>();
                SetVector(new Half3(h.X, h.Y, (Half)value));
                break;
            default:
                throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}");
        }
    }
}