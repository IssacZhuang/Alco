using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Alco.Engine;
using Alco.IO;
using Alco.Project;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alco.Editor.Views;

public partial class ProjectPage : UserControl
{
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public ProjectPage()
    {
        InitializeComponent();
        var configReferenceResolver = new ConfigReferenceResolver(App.Main.Engine.Assets);
        _jsonSerializerOptions = Configable.BuildJsonSerializerOptions(configReferenceResolver);
        _jsonSerializerOptions.WriteIndented = true;

        if (App.Main.Engine.IsProjectOpen)
        {
            OnProjectOpened(App.Main.Engine.Project!);
        }

        EditorEngine editorEngine = App.Main.Engine;
        editorEngine.OnProjectOpened += OnProjectOpened;
        editorEngine.OnProjectClosed += OnProjectClosed;
    }

    private void OnProjectOpened(AlcoProject project)
    {
        AlcoProjectConfig config = project.Config;
        ViewModels.ObjectPropertiesEditor objectPropertiesEditor = new(config, "Project Config");
        ObjectPropertiesEditor.DataContext = objectPropertiesEditor;
        objectPropertiesEditor.OnValueChanged += () => PrintJson(config);
    }

    private void OnProjectClosed(AlcoProject project)
    {
        ObjectPropertiesEditor.DataContext = null;
    }

    private void PrintJson(object obj)
    {

        string json = JsonSerializer.Serialize(obj, _jsonSerializerOptions);
        Console.WriteLine(json);
    }
}