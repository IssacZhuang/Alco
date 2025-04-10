using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace Alco.Editor.Views;

public partial class TestGpuWindow : Window
{
    public TestGpuWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // Additional initialization if needed
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        GpuView.Dispose();
    }
}