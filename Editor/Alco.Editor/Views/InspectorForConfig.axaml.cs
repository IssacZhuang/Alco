using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Diagnostics;
using Alco.IO;
using Avalonia.Input;
using Avalonia.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
        vmPropertiesEditor.OnValueChanged += OnRefresh;
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
        IEnumerable<string> errors = viewModel.Validate(App.Main.Engine);
        if (errors.Any())
        {
            TextError.Text = string.Join("\n", errors);
            TextError.IsVisible = true;
        }
        else
        {
            TextError.IsVisible = false;
        }
    }

    private void OnGridSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        Log.Info(PropertiesEditor.Width);
    }
}