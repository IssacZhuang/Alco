using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Diagnostics;
using Alco.IO;

namespace Alco.Editor.Views;

public partial class InspectorForConfig : UserControl
{

    public ViewModels.InspectorForConfig ViewModel => DataContext as ViewModels.InspectorForConfig ?? throw new InvalidOperationException("DataContext is not a ViewModels.InspectorForConfig");

    public InspectorForConfig()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BaseConfig? config = ViewModel.Asset;
        if (config == null)
        {
            return;
        }
        
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TextJsonPreview.Text = ViewModel.SerializedJson;
    }
}