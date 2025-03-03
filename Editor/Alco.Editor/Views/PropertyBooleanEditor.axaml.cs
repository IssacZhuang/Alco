using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class PropertyBooleanEditor : UserControl
{
    public PropertyBooleanEditor()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyBooleanEditor viewModel)
        {
            Setup(viewModel);
        }
    }

    private void Setup(ViewModels.PropertyBooleanEditor viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (!viewModel.CanRead)
        {
            return;
        }

        InputCheckBox.Bind(CheckBox.IsCheckedProperty, new Binding(viewModel.PropertyName)
        {
            Source = viewModel.Target,
        });

        if (viewModel.CanWrite)
        {
            InputCheckBox.IsCheckedChanged += (sender, e) =>
            {
                viewModel.Refresh();
            };
        }
        else
        {
            InputCheckBox.IsEnabled = false;
        }
    }
} 