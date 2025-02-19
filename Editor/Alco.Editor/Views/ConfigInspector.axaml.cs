using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class ConfigInspector : UserControl
{

    public ViewModels.ConfigInspector ViewModel => DataContext as ViewModels.ConfigInspector ?? throw new InvalidOperationException("DataContext is not a ViewModels.ExplorerPage");

    public ConfigInspector()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TextJsonPreview.Text = ViewModel.SerializedJson;

    }
}