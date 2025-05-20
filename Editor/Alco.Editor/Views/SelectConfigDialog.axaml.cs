using Alco.Editor.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Alco.Editor.Views;

public partial class SelectConfigDialog : Window
{
    public SelectConfigDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ViewModels.SelectConfigDialog viewModel)
        {
            return;
        }

        ConfigExplorer.DataContext = viewModel.ConfigExplorer;
    }

    public void OnBtnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.SelectConfigDialog viewModel)
        {
            return;
        }

        if (viewModel.Confirm())
        {
            Close();
        }
    }

    public void OnBtnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

