using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace Alco.Editor.Views;

public partial class ConfirmDialog : Window
{
    public bool? Result { get; private set; }

    public ConfirmDialog()
    {
        InitializeComponent();

        BtnConfirm.Click += OnBtnConfirmClick;
        BtnCancel.Click += OnBtnCancelClick;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ViewModels.ConfirmDialog? viewModel = DataContext as ViewModels.ConfirmDialog;
        if (viewModel == null)
        {
            return;
        }

        TextError.Text = "";
        TextMessage.Text = viewModel.Message;
    }

    public void OnBtnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Result = true;
        if (DataContext is not ViewModels.ConfirmDialog viewModel)
        {
            return;
        }

        try
        {
            viewModel.DoConfirm();
        }
        catch (Exception ex)
        {
            TextError.Text = ex.Message;
        }
        Close();
    }

    public void OnBtnCancelClick(object? sender, RoutedEventArgs e)
    {
        Result = false;
        if (DataContext is not ViewModels.ConfirmDialog viewModel)
        {
            return;
        }

        try
        {
            viewModel.DoCancel();
        }
        catch (Exception ex)
        {
            TextError.Text = ex.Message;
        }
        Close();
    }
}