using System;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class PropertyListEditor : UserControl
{
    private readonly StringBuilder _builder = new();
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
            Root.Children.Clear();
        }
    }

    private void Setup(ViewModels.PropertyListEditor viewModel)
    {
        RefreshList(viewModel);

        BtnAdd.Click += Add;
        BtnRemove.Click += RemoveLast;
        BtnClear.Click += Clear;
        viewModel.OnUIChanged += RefreshList;
    }

    private void RefreshList(ViewModels.PropertyListEditor viewModel)
    {
        Root.Children.Clear();
        foreach (var (_, control) in viewModel.ItemEditors)
        {
            Root.Children.Add(control);
        }
        UpdateTitle(viewModel);
    }

    private void Clear(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.PropertyListEditor viewModel)
        {
            viewModel.Clear();
        }
    }

    private void Add(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.PropertyListEditor viewModel)
        {
            viewModel.Add();
        }
    }

    private void RemoveLast(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.PropertyListEditor viewModel)
        {
            viewModel.RemoveLast();
        }
    }

    private void UpdateTitle(ViewModels.PropertyListEditor viewModel)
    {
        _builder.Clear();
        _builder.Append(viewModel.Header);
        _builder.Append(", Count = ");
        _builder.Append(viewModel.ItemCount);
        TextHeader.Text = _builder.ToString();
    }
}