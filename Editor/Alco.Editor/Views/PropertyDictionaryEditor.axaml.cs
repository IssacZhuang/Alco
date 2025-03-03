using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class PropertyDictionaryEditor : UserControl
{
    public PropertyDictionaryEditor()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyDictionaryEditor viewModel)
        {
            Setup(viewModel);
        }
        else
        {
            Clear();
        }
    }

    private void Setup(ViewModels.PropertyDictionaryEditor viewModel)
    {
        RefreshList(viewModel);

        BtnAdd.Click += Add;
    }

    private void RefreshList(ViewModels.PropertyDictionaryEditor viewModel)
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
        if (DataContext is ViewModels.PropertyDictionaryEditor viewModel)
        {
            viewModel.Add();
            RefreshList(viewModel);
        }
    }
}