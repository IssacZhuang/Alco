using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Alco.Editor.Views;

public partial class CreateFolderDialog : Window
{
    public string? FolderName { get; private set; }

    public CreateFolderDialog()
    {
        InitializeComponent();

        BtnConfirm.Click += OnBtnConfirmClick;
        BtnCancel.Click += OnBtnCancelClick;

        if (!Design.IsDesignMode)
        {
            TextError.Text = "";
        }
    }

    public void OnBtnConfirmClick(object? sender, RoutedEventArgs e)
    {
        ViewModels.CreateFolderDialog? viewModel = DataContext as ViewModels.CreateFolderDialog;
        if (viewModel == null)
        {
            return;
        }

        string targetPath = File.Exists(viewModel.Path)
            ? Path.GetDirectoryName(viewModel.Path) ?? string.Empty
            : viewModel.Path;

        string fullPath = Path.Combine(targetPath, InputFolderName.Text ?? string.Empty);

        try
        {
            Directory.CreateDirectory(fullPath);
            Close();
        }
        catch (Exception ex)
        {
            TextError.Text = ex.Message;
        }
    }

    public void OnBtnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}