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
public class PropertyVector3Editor : PropertyEditor
{
    public PropertyVector3Editor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
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

    public override Control CreateControl()
    {
        Control control = new Views.PropertyVector3Editor()
        {
            DataContext = this,
        };
        return control;
    }

    public string GetFormatString()
    {
        return MemberInfo.MemberType switch
        {
            Type t when t == typeof(Vector3) => "G",
            Type t when t == typeof(int3) => "F0",
            Type t when t == typeof(uint3) => "F0",
            Type t when t == typeof(Half3) => "G",
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
            Type t when t == typeof(Vector3) => (decimal)GetVector<Vector3>().X,
            Type t when t == typeof(int3) => (decimal)GetVector<int3>().X,
            Type t when t == typeof(uint3) => (decimal)GetVector<uint3>().X,
            Type t when t == typeof(Half3) => (decimal)GetVector<Half3>().X,
            _ => throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}")
        };
    }

    private decimal GetValueY()
    {
        return MemberInfo.MemberType switch
        {
            Type t when t == typeof(Vector3) => (decimal)GetVector<Vector3>().Y,
            Type t when t == typeof(int3) => (decimal)GetVector<int3>().Y,
            Type t when t == typeof(uint3) => (decimal)GetVector<uint3>().Y,
            Type t when t == typeof(Half3) => (decimal)GetVector<Half3>().Y,
            _ => throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}")
        };
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

    private void SetValueX(decimal value)
    {
        switch (MemberInfo.MemberType)
        {
            case Type t when t == typeof(Vector3):
                Vector3 v = GetVector<Vector3>();
                SetVector(new Vector3((float)value, v.Y, v.Z));
                break;
            case Type t when t == typeof(int3):
                int3 i = GetVector<int3>();
                SetVector(new int3((int)value, i.Y, i.Z));
                break;
            case Type t when t == typeof(uint3):
                uint3 u = GetVector<uint3>();
                SetVector(new uint3((uint)value, u.Y, u.Z));
                break;
            case Type t when t == typeof(Half3):
                Half3 h = GetVector<Half3>();
                SetVector(new Half3((Half)value, h.Y, h.Z));
                break;
            default:
                throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}");
        }
        DoValueChangedEvent();
    }

    private void SetValueY(decimal value)
    {
        switch (MemberInfo.MemberType)
        {
            case Type t when t == typeof(Vector3):
                Vector3 v = GetVector<Vector3>();
                SetVector(new Vector3(v.X, (float)value, v.Z));
                break;
            case Type t when t == typeof(int3):
                int3 i = GetVector<int3>();
                SetVector(new int3(i.X, (int)value, i.Z));
                break;
            case Type t when t == typeof(uint3):
                uint3 u = GetVector<uint3>();
                SetVector(new uint3(u.X, (uint)value, u.Z));
                break;
            case Type t when t == typeof(Half3):
                Half3 h = GetVector<Half3>();
                SetVector(new Half3(h.X, (Half)value, h.Z));
                break;
            default:
                throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}");
        }
        DoValueChangedEvent();
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
        DoValueChangedEvent();
    }
}