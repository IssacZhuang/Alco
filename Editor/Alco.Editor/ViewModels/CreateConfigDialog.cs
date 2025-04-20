

using System;
using System.Collections.Generic;
using System.Linq;
using Alco.Editor.Models;
using Alco.IO;

namespace Alco.Editor.ViewModels;

public class CreateConfigDialog : ViewModelBase
{
    private readonly List<Type> _types;
    private string? _warning;
    private bool _hasWarning;
    private string _path;
    
    public ConfigTypeExplorer ConfigTypeExplorer { get; }
    public string Filename { get; set; } = string.Empty;


    public bool HasWarning 
    {
        get => !string.IsNullOrEmpty(Warning);
        set => SetProperty(ref _hasWarning, value);
    }
    public string? Warning { 
        get => _warning;
        set => SetProperty(ref _warning, value);
    }


    public CreateConfigDialog(string path, params Type[] types)
    {
        _types = types.ToList();
        _path = path;
        ConfigTypeExplorer = new ConfigTypeExplorer(types);
    }

    public Views.CreateConfigDialog CreateControl()
    {
        return new Views.CreateConfigDialog
        {
            DataContext = this
        };
    }

    public bool Confirm()
    {
        if (ConfigTypeExplorer.SelectedItem == null)
        {
            Warning = "Please select a type";
            HasWarning = true;
            return false;
        }

        ExplorerItem item = ConfigTypeExplorer.SelectedItem;
        Type? type = item.UserData as Type;

        if (type == null)
        {
            Warning = "Type not found";
            HasWarning = true;
            return false;
        }

        if (string.IsNullOrEmpty(Filename))
        {
            Warning = "Please enter a filename";
            HasWarning = true;
            return false;
        }

        Configable? instance = Activator.CreateInstance(type) as Configable ?? throw new Exception($"The type {type.Name} is not a valid config type.");
        App.CurrentProject?.WriteConfig(instance, _path, Filename);
        return true;
    }
}
