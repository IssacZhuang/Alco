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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyEditorNumber viewModel)
        {
            Bind(viewModel.Target, viewModel.MemberInfo);
        }
    }

    public void Bind(object target, AccessMemberInfo memberInfo)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(memberInfo);

        InputNumber.Bind(NumericUpDown.ValueProperty, new Binding(memberInfo.Name)
        {
            Source = target,
        });

        InputNumber.FormatString = GetFormatString(memberInfo.MemberType);
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