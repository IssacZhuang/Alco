using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Diagnostics;

namespace Alco.Editor.Views;

public partial class ConfigInspector : UserControl
{

    public ViewModels.ConfigInspector ViewModel => DataContext as ViewModels.ConfigInspector ?? throw new InvalidOperationException("DataContext is not a ViewModels.ConfigInspector");

    public ConfigInspector()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        TextJsonPreview.Text = ViewModel.SerializedJson;

        
    }
}