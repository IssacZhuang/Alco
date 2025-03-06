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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.PropertyEditorException propertyEditorException)
        {
            TextException.Text = propertyEditorException.Message;
        }
    }
}