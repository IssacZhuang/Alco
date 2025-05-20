
using System;
using Alco.Project;

namespace Alco.Editor.ViewModels;

public class SelectConfigDialog : ViewModelBase
{
    private string? _warning;
    private bool _hasWarning;

    public ConfigExplorer ConfigExplorer { get; }

    public bool HasWarning
    {
        get => !string.IsNullOrEmpty(Warning);
        set => SetProperty(ref _hasWarning, value);
    }
    public string? Warning
    {
        get => _warning;
        set => SetProperty(ref _warning, value);
    }

    public event EventHandler<ConfigMeta>? OnConfirm;


    public SelectConfigDialog(ConfigMeta[] configs)
    {
        ConfigExplorer = new ConfigExplorer(configs);
    }

    public bool Confirm()
    {
        ConfigMeta? configMeta = ConfigExplorer.SelectedItem?.UserData as ConfigMeta;
        if (configMeta == null)
        {
            Warning = "Please select a config";
            HasWarning = true;
            return false;
        }

        OnConfirm?.Invoke(this, configMeta);
        return true;
    }
}

