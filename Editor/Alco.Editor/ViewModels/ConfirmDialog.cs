using System;

namespace Alco.Editor.ViewModels;

public class ConfirmDialog : ViewModelBase
{
    public string Message { get; }

    public event Action? OnConfirm;
    public event Action? OnCancel;

    public ConfirmDialog(string message)
    {
        Message = message;
    }

    public Views.ConfirmDialog CreateWindow()
    {
        return new Views.ConfirmDialog
        {
            DataContext = this
        };
    }

    public void DoConfirm()
    {
        OnConfirm?.Invoke();
    }

    public void DoCancel()
    {
        OnCancel?.Invoke();
    }
    
}