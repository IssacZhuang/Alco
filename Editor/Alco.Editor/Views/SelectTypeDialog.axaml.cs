using Alco.Editor.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Alco.Editor.Views;

public partial class SelectTypeDialog : Window
{
    public SelectTypeDialog()
    {
        InitializeComponent();
    }

    private void OnTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        
    }

    public void OnBtnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.SelectTypeDialog viewModel)
        {
            return;
        }

        viewModel.Confirm();
        Close();
    }

    public void OnBtnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

