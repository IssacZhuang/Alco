using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;


public partial class PropertyVector4Editor : UserControl
{
    public PropertyVector4Editor()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyVector4Editor viewModel)
        {
            Setup(viewModel);
        }
    }

    private void Setup(ViewModels.PropertyVector4Editor viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        AccessMemberInfo memberInfo = viewModel.MemberInfo;

        if (!memberInfo.CanRead)
        {
            return;
        }

        string formatString = viewModel.GetFormatString();

        InputX.Bind(NumericUpDown.ValueProperty, new Binding(nameof(viewModel.X))
        {
            Source = viewModel,
        });
        InputX.FormatString = formatString;

        InputY.Bind(NumericUpDown.ValueProperty, new Binding(nameof(viewModel.Y))
        {
            Source = viewModel,
        });
        InputY.FormatString = formatString;

        InputZ.Bind(NumericUpDown.ValueProperty, new Binding(nameof(viewModel.Z))
        {
            Source = viewModel,
        });
        InputZ.FormatString = formatString;

        InputW.Bind(NumericUpDown.ValueProperty, new Binding(nameof(viewModel.W))
        {
            Source = viewModel,
        });
        InputW.FormatString = formatString;
    }
}