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

    private decimal GetValueX()
    {
        if (MemberInfo.MemberType == typeof(Vector2))
        {
            return (decimal)MemberInfo.GetValue<Vector2>(Target).X;
        }
        else if (MemberInfo.MemberType == typeof(int2))
        {
            return (decimal)MemberInfo.GetValue<int2>(Target).X;
        }
        else if (MemberInfo.MemberType == typeof(uint2))
        {
            return (decimal)MemberInfo.GetValue<uint2>(Target).X;
        }
        else if (MemberInfo.MemberType == typeof(Half2))
        {
            return (decimal)MemberInfo.GetValue<Half2>(Target).X;
        }
        throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}");
    }

    private decimal GetValueY()
    {
        if (MemberInfo.MemberType == typeof(Vector2))
        {
            return (decimal)MemberInfo.GetValue<Vector2>(Target).Y;
        }
        else if (MemberInfo.MemberType == typeof(int2))
        {
            return (decimal)MemberInfo.GetValue<int2>(Target).Y;
        }
        else if (MemberInfo.MemberType == typeof(uint2))
        {
            return (decimal)MemberInfo.GetValue<uint2>(Target).Y;
        }
        else if (MemberInfo.MemberType == typeof(Half2))
        {
            return (decimal)MemberInfo.GetValue<Half2>(Target).Y;
        }
        throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}");
    }

    private void SetValueX(decimal value)
    {
        if (MemberInfo.MemberType == typeof(Vector2))
        {
            Vector2 vector2 = MemberInfo.GetValue<Vector2>(Target);
            MemberInfo.SetValue(Target, new Vector2((float)value, vector2.Y));
        }
        else if (MemberInfo.MemberType == typeof(int2))
        {
            int2 int2 = MemberInfo.GetValue<int2>(Target);
            MemberInfo.SetValue(Target, new int2((int)value, int2.Y));
        }
        else if (MemberInfo.MemberType == typeof(uint2))
        {
            uint2 uint2 = MemberInfo.GetValue<uint2>(Target);
            MemberInfo.SetValue(Target, new uint2((uint)value, uint2.Y));
        }
        else if (MemberInfo.MemberType == typeof(Half2))    
        {
            Half2 half2 = MemberInfo.GetValue<Half2>(Target);
            MemberInfo.SetValue(Target, new Half2((Half)value, half2.Y));
        }
        throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}");
    }

    private void SetValueY(decimal value)
    {
        if (MemberInfo.MemberType == typeof(Vector2))
        {
            Vector2 vector2 = MemberInfo.GetValue<Vector2>(Target);
            MemberInfo.SetValue(Target, new Vector2(vector2.X, (float)value));
        }
        else if (MemberInfo.MemberType == typeof(int2))
        {
            int2 int2 = MemberInfo.GetValue<int2>(Target);
            MemberInfo.SetValue(Target, new int2(int2.X, (int)value));
        }
        else if (MemberInfo.MemberType == typeof(uint2))
        {
            uint2 uint2 = MemberInfo.GetValue<uint2>(Target);
            MemberInfo.SetValue(Target, new uint2(uint2.X, (uint)value));
        }
        else if (MemberInfo.MemberType == typeof(Half2))
        {
            Half2 half2 = MemberInfo.GetValue<Half2>(Target);
            MemberInfo.SetValue(Target, new Half2(half2.X, (Half)value));
        }
        throw new InvalidOperationException($"Unsupported type: {MemberInfo.MemberType}");
    }
    
    

}
