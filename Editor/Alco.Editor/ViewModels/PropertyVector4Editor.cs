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
public class PropertyVector4Editor : PropertyEditor
{
    public PropertyVector4Editor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
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

    public decimal Z
    {
        get => GetValueZ();
        set => SetValueZ(value);
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

    public string GetFormatString()
    {
        return MemberInfo.MemberType switch
        {
            Type t when t == typeof(Vector4) => "G",
            Type t when t == typeof(int4) => "F0",
            Type t when t == typeof(uint4) => "F0",
            Type t when t == typeof(Half4) => "G",
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
        return MemberInfo.MemberType switch
        {
            Type t when t == typeof(Vector4) => (decimal)GetVector<Vector4>().X,
            Type t when t == typeof(int4) => (decimal)GetVector<int4>().X,
            Type t when t == typeof(uint4) => (decimal)GetVector<uint4>().X,
            Type t when t == typeof(Half4) => (decimal)GetVector<Half4>().X,
            _ => throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}")
        };
    }

    private decimal GetValueY()
    {
        return MemberInfo.MemberType switch
        {
            Type t when t == typeof(Vector4) => (decimal)GetVector<Vector4>().Y,
            Type t when t == typeof(int4) => (decimal)GetVector<int4>().Y,
            Type t when t == typeof(uint4) => (decimal)GetVector<uint4>().Y,
            Type t when t == typeof(Half4) => (decimal)GetVector<Half4>().Y,
            _ => throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}")
        };
    }

    private decimal GetValueZ()
    {
        return MemberInfo.MemberType switch
        {
            Type t when t == typeof(Vector4) => (decimal)GetVector<Vector4>().Z,
            Type t when t == typeof(int4) => (decimal)GetVector<int4>().Z,
            Type t when t == typeof(uint4) => (decimal)GetVector<uint4>().Z,
            Type t when t == typeof(Half4) => (decimal)GetVector<Half4>().Z,
            _ => throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}")
        };
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

    private void SetValueX(decimal value)
    {
        switch (MemberInfo.MemberType)
        {
            case Type t when t == typeof(Vector4):
                Vector4 v = GetVector<Vector4>();
                SetVector(new Vector4((float)value, v.Y, v.Z, v.W));
                break;
            case Type t when t == typeof(int4):
                int4 i = GetVector<int4>();
                SetVector(new int4((int)value, i.Y, i.Z, i.W));
                break;
            case Type t when t == typeof(uint4):
                uint4 u = GetVector<uint4>();
                SetVector(new uint4((uint)value, u.Y, u.Z, u.W));
                break;
            case Type t when t == typeof(Half4):
                Half4 h = GetVector<Half4>();
                SetVector(new Half4((Half)value, h.Y, h.Z, h.W));
                break;
            default:
                throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}");
        }
        Refresh();
    }

    private void SetValueY(decimal value)
    {
        switch (MemberInfo.MemberType)
        {
            case Type t when t == typeof(Vector4):
                Vector4 v = GetVector<Vector4>();
                SetVector(new Vector4(v.X, (float)value, v.Z, v.W));
                break;
            case Type t when t == typeof(int4):
                int4 i = GetVector<int4>();
                SetVector(new int4(i.X, (int)value, i.Z, i.W));
                break;
            case Type t when t == typeof(uint4):
                uint4 u = GetVector<uint4>();
                SetVector(new uint4(u.X, (uint)value, u.Z, u.W));
                break;
            case Type t when t == typeof(Half4):
                Half4 h = GetVector<Half4>();
                SetVector(new Half4(h.X, (Half)value, h.Z, h.W));
                break;
            default:
                throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}");
        }
        Refresh();
    }

    private void SetValueZ(decimal value)
    {
        switch (MemberInfo.MemberType)
        {
            case Type t when t == typeof(Vector4):
                Vector4 v = GetVector<Vector4>();
                SetVector(new Vector4(v.X, v.Y, (float)value, v.W));
                break;
            case Type t when t == typeof(int4):
                int4 i = GetVector<int4>();
                SetVector(new int4(i.X, i.Y, (int)value, i.W));
                break;
            case Type t when t == typeof(uint4):
                uint4 u = GetVector<uint4>();
                SetVector(new uint4(u.X, u.Y, (uint)value, u.W));
                break;
            case Type t when t == typeof(Half4):
                Half4 h = GetVector<Half4>();
                SetVector(new Half4(h.X, h.Y, (Half)value, h.W));
                break;
            default:
                throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}");
        }
        Refresh();
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
        Refresh();
    }
}