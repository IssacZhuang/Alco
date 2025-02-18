using System;
using System.Collections.Generic;


namespace Alco.Editor.Models;


public class ContextMenuItemInfo
{
    public string Header { get; set; } = string.Empty;
    public Action<Avalonia.Controls.MenuItem>? Action { get; set; }
    public Dictionary<string, ContextMenuItemInfo> Child { get; set; } = new();
}