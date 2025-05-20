using System;
using System.Numerics;
using Alco.Editor.Attributes;
using Alco.Graphics;
using Avalonia.Controls;
using Avalonia.Media;

namespace Alco.Editor.ViewModels;

/// <summary>
/// ViewModel for editing a ColorFloat property
/// </summary>
[PropertyEditor(typeof(ColorFloat))]
public class PropertyColorFloatEditor : PropertyEditor
{
    private SolidColorBrush? _colorBrush;

    public PropertyColorFloatEditor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }

    public float R
    {
        get => GetColorValue().R;
        set => SetColorValue(new ColorFloat(value, G, B, A));
    }

    public float G
    {
        get => GetColorValue().G;
        set => SetColorValue(new ColorFloat(R, value, B, A));
    }

    public float B
    {
        get => GetColorValue().B;
        set => SetColorValue(new ColorFloat(R, G, value, A));
    }

    public float A
    {
        get => GetColorValue().A;
        set => SetColorValue(new ColorFloat(R, G, B, value));
    }

    public ColorFloat ColorFloat
    {
        get => GetColorValue();
        set => SetColorValue(value);
    }

    /// <summary>
    /// Convert the ColorFloat to an Avalonia Color for display
    /// </summary>
    public IBrush ColorBrush
    {
        get
        {
            _colorBrush ??= new SolidColorBrush();
            ColorFloat color = GetColorValue();
            color.Decompose(out Color32 color32, out float _);
            _colorBrush.Color = Color.FromArgb(
                (byte)(color32.A),
                (byte)(color32.R),
                (byte)(color32.G),
                (byte)(color32.B)
            );
            return _colorBrush;
        }
    }

    private ColorFloat GetColorValue()
    {
        return MemberInfo.GetValue<ColorFloat>(Target);
    }

    private void SetColorValue(ColorFloat value)
    {
        MemberInfo.SetValue(Target, value);
        OnPropertyChanged(nameof(R));
        OnPropertyChanged(nameof(G));
        OnPropertyChanged(nameof(B));
        OnPropertyChanged(nameof(A));
        OnPropertyChanged(nameof(ColorBrush));
        DoValueChangedEvent();
    }

    /// <summary>
    /// Create the editor control
    /// </summary>
    /// <returns>The color editor control</returns>
    public override Control CreateControl()
    {
        return new Views.PropertyColorFloatEditor { DataContext = this };
    }
}