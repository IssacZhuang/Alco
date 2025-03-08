using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Diagnostics;
using Alco.IO;
using Avalonia.Input;

namespace Alco.Editor.Views;

public partial class InspectorForConfig : UserControl
{
    public InspectorForConfig()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ViewModels.InspectorForConfig viewModel)
        {
            return;
        }

        BaseConfig? config = viewModel.Asset;
        if (config == null)
        {
            return;
        }

        ViewModels.ObjectPropertiesEditor vmPropertiesEditor = new(config, config.Id, 0);
        PropertiesEditor.DataContext = vmPropertiesEditor;
        PropertiesEditor.IsExpanded = true;
        vmPropertiesEditor.OnRefresh += OnRefresh;
        OnRefresh();
    }

    private void OnRefresh()
    {
        if (DataContext is not ViewModels.InspectorForConfig viewModel)
        {
            return;
        }

        viewModel.RefreshSerializedJson(App.Main.Engine);
        TextJsonPreview.Text = viewModel.SerializedJson;
    }

    private void OnGridSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        Log.Info(PropertiesEditor.Width);
    }
}