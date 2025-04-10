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
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using Avalonia.Interactivity;

namespace Alco.Editor.Views;

public partial class InspectorForConfig : UserControl
{

    public InspectorForConfig()
    {
        InitializeComponent();
        var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        var textMateInstallation = TextJsonPreview.InstallTextMate(registryOptions);
        textMateInstallation.SetGrammar(registryOptions.GetScopeByLanguageId(registryOptions.GetLanguageByExtension(".json").Id));

        KeyDown += OnKeyDown;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ViewModels.InspectorForConfig viewModel)
        {
            return;
        }

        Configable? config = viewModel.Asset;
        if (config == null)
        {
            return;
        }

        if (viewModel.PropertiesEditor is null)
        {
            return;
        }

        PropertiesEditor.DataContext = viewModel.PropertiesEditor;
        PropertiesEditor.IsExpanded = true;
        viewModel.PropertiesEditor.OnValueChanged += Refresh;
        Refresh();
    }

    private void Refresh()
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
        

    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            SaveDocument();
            e.Handled = true;
        }
        
    }

    private void SaveDocument()
    {
        if (DataContext is not ViewModels.InspectorForConfig viewModel)
        {
            return;
        }

        if (!viewModel.IsModified)
        {
            Log.Info($"{viewModel.Filename} has nothing to save");
            return;
        }

        viewModel.SaveAsset(App.Main.Engine);
        Refresh();
    }

    private void OnBtnSaveClick(object? sender, RoutedEventArgs e)
    {
        SaveDocument();
    }

    private void OnBtnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.InspectorForConfig viewModel)
        {
            return;
        }
    }




}