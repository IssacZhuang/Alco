using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class PropertyEditorNumber : UserControl
{
    public PropertyEditorNumber()
    {
        InitializeComponent();
    }

    public void Bind(object target, string propertyPath)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(propertyPath);

        var property = target.GetType().GetProperty(propertyPath) ?? throw new ArgumentException($"Property '{propertyPath}' not found on target type '{target.GetType().Name}'");
        InputNumber.Bind(NumericUpDown.ValueProperty, new Binding(propertyPath)
        {
            Source = target,
        });

        InputNumber.FormatString = GetFormatString(property.PropertyType);
    }

    private static string GetFormatString(Type type)
    {
        return Type.GetTypeCode(type) switch
        {

            TypeCode.SByte or TypeCode.Byte or
            TypeCode.Int16 or TypeCode.UInt16 or
            TypeCode.Int32 or TypeCode.UInt32 or
            TypeCode.Int64 or TypeCode.UInt64 => "F0",

            TypeCode.Single => "F4",  // float
            TypeCode.Double => "F4",  // double

            TypeCode.Decimal => "F8",

            _ => "G"
        };
    }
}