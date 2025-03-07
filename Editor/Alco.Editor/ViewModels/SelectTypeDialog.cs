

using System;
using System.Collections.Generic;
using System.Linq;
using Alco.Editor.Models;

namespace Alco.Editor.ViewModels;

public class CraeteConfigDialog : ViewModelBase
{
    private readonly List<Type> _types;
    public ConfigTypeExplorer ConfigTypeExplorer { get; }

    public event Action<Type>? OnTypeConfirmed;


    public CraeteConfigDialog(params Type[] types)
    {
        _types = types.ToList();
        ConfigTypeExplorer = new ConfigTypeExplorer(types);
    }

    public Views.CraeteConfigDialog CreateControl()
    {
        return new Views.CraeteConfigDialog
        {
            DataContext = this
        };
    }

    public void Confirm()
    {
        if (ConfigTypeExplorer.SelectedItem == null)
        {
            return;
        }

        ExplorerItem item = ConfigTypeExplorer.SelectedItem;
        Type type = _types.FirstOrDefault(t => t.FullName == item.Path) ?? throw new Exception($"Type {item.Path} not found");

        OnTypeConfirmed?.Invoke(type);
    }
}
