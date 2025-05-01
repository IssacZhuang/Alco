using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Alco.Editor.Views;

public partial class PropertyColorFloatEditor : UserControl
{
    public PropertyColorFloatEditor()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyColorFloatEditor viewModel)
        {
            Setup(viewModel);
        }
    }

    private void Setup(ViewModels.PropertyColorFloatEditor viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        AccessMemberInfo memberInfo = viewModel.MemberInfo;

        if (!memberInfo.CanRead)
        {
            return;
        }

        // Bind the rectangle's fill to the color in the view model
        ColorDisplay.Bind(Rectangle.FillProperty, new Binding(nameof(viewModel.ColorBrush))
        {
            Source = viewModel,
        });


        InputR.Bind(NumericUpDown.ValueProperty, new Binding(nameof(viewModel.R))
        {
            Source = viewModel,
        });

        InputG.Bind(NumericUpDown.ValueProperty, new Binding(nameof(viewModel.G))
        {
            Source = viewModel,
        });

        InputB.Bind(NumericUpDown.ValueProperty, new Binding(nameof(viewModel.B))
        {
            Source = viewModel,
        });

        InputA.Bind(NumericUpDown.ValueProperty, new Binding(nameof(viewModel.A))
        {
            Source = viewModel,
        });


    }
}