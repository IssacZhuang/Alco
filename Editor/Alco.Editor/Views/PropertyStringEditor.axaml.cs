using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class PropertyStringEditor : UserControl
{
    public PropertyStringEditor()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyStringEditor viewModel)
        {
            Setup(viewModel);
        }
    }

    private void Setup(ViewModels.PropertyStringEditor viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (!viewModel.CanRead)
        {
            return;
        }

        InputText.Bind(TextBox.TextProperty, new Binding(viewModel.PropertyName)
        {
            Source = viewModel.Target,
        });

        if (viewModel.CanWrite)
        {
            InputText.TextChanged += (sender, e) =>
            {
                viewModel.Refresh();
            };
        }
        else
        {
            InputText.IsReadOnly = true;
        }


    }
}