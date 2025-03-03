using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
        RefreshList(viewModel);

        BtnAdd.Click += Add;
        BtnRemove.Click += RemoveLast;
    }

    private void RefreshList(ViewModels.PropertyListEditor viewModel)
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

    private void Add(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.PropertyListEditor viewModel)
        {
            viewModel.Add();
            RefreshList(viewModel);
        }
    }

    private void RemoveLast(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.PropertyListEditor viewModel)
        {
            viewModel.RemoveLast();
            RefreshList(viewModel);
        }
    }
}