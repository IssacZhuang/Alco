using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class PropertyEditorException : UserControl
{
    public PropertyEditorException()
    {
        InitializeComponent();
        TextException.Text = "This is an exception";
    }

    public void SetException(string exception)
    {
        TextException.Text = exception;
    }
}