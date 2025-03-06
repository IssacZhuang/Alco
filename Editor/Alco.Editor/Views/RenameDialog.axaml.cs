using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.IO;

namespace Alco.Editor.Views;

public partial class RenameDialog : Window
{
    public string? NewName { get; private set; }

    public RenameDialog()
    {
        InitializeComponent();

        BtnConfirm.Click += OnBtnConfirmClick;
        BtnCancel.Click += OnBtnCancelClick;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ViewModels.RenameDialog? viewModel = DataContext as ViewModels.RenameDialog;
        if (viewModel == null)
        {
            return;
        }

        TextError.Text = "";
        InputNewName.Text = viewModel.OriginalName;
    }

    public void OnBtnConfirmClick(object? sender, RoutedEventArgs e)
    {
        ViewModels.RenameDialog? viewModel = DataContext as ViewModels.RenameDialog;
        if (viewModel == null)
        {
            return;
        }

        string newName = InputNewName.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newName))
        {
            TextError.Text = "Please enter a valid name";
            return;
        }

        string directory = Path.GetDirectoryName(viewModel.Path) ?? string.Empty;
        string newPath = Path.Combine(directory, newName);

        try
        {
            if (File.Exists(viewModel.Path))
            {
                if (File.Exists(newPath))
                {
                    TextError.Text = "A file with this name already exists";
                    return;
                }
                File.Move(viewModel.Path, newPath);
            }
            else if (Directory.Exists(viewModel.Path))
            {
                if (Directory.Exists(newPath))
                {
                    TextError.Text = "A folder with this name already exists";
                    return;
                }
                Directory.Move(viewModel.Path, newPath);
            }
            else
            {
                TextError.Text = "The original file or folder no longer exists";
                return;
            }

            NewName = newName;
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