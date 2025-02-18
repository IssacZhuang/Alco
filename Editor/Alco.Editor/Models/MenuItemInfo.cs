using System;
using System.Collections.Generic;


namespace Alco.Editor.Models;

public class MenuItemInfo
{
    public string Header { get; set; } = string.Empty;
    public Action<Avalonia.Controls.Window>? Action { get; set; }
    public Dictionary<string, MenuItemInfo> Child { get; set; } = new();
}

public class ContextMenuItemInfo
{
    public string Header { get; set; } = string.Empty;
    public Action<Avalonia.Controls.MenuItem>? Action { get; set; }
    public Dictionary<string, ContextMenuItemInfo> Child { get; set; } = new();
}