using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class PropertyNumberEditor : UserControl
{
    public PropertyNumberEditor()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyNumberEditor viewModel)
        {
            Setup(viewModel);
        }
    }

    private void Setup(ViewModels.PropertyNumberEditor viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        AccessMemberInfo memberInfo = viewModel.MemberInfo;

        if (!memberInfo.CanRead)
        {
            return;
        }

        InputNumber.Bind(NumericUpDown.ValueProperty, new Binding(memberInfo.Name)
        {
            Source = viewModel.Target,
        });
        InputNumber.FormatString = viewModel.GetFormatString();

        if (memberInfo.CanWrite)
        {
            //writable
            InputNumber.ValueChanged += (sender, e) =>
            {
                viewModel.Refresh();
            };
        }
        else
        {
            //readonly
            InputNumber.IsReadOnly = true;
        }

    }
}