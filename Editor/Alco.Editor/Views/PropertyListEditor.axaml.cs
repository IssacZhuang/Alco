using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class PropertyListEditor : UserControl
{
    public PropertyListEditor()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyListEditor viewModel)
        {
            Setup(viewModel);
        }
        else
        {
            Clear();
        }
    }

    private void Setup(ViewModels.PropertyListEditor viewModel)
    {
        Root.Children.Clear();
        foreach (var item in viewModel.ItemEditors)
        {
            Root.Children.Add(item);
        }
    }

    private void Clear()
    {
        Root.Children.Clear();
    }
}