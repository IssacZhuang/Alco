using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Alco.Editor.Attributes;
using Alco.Editor.Models;
using Avalonia.Controls;
using Alco;
using System.Linq;
using Alco.IO;
using Alco.Project;

namespace Alco.Editor.ViewModels;

public class ConfigExplorer : FileExplorer
{
    private readonly List<ExplorerItem> _items = [];
    private readonly List<ExplorerItem> _searchResults = [];
    private ContextMenu? _contextMenu;

    public ExplorerItem? SelectedItem { get; set; }

    public ConfigExplorer(params ConfigMeta[] configs)
    {
        _items.AddRange(configs.Select(t => new ExplorerItem
        {
            Type = ExplorerItemType.Type,
            Path = t.Path,
            UserData = t
        }));
    }

    public override ContextMenu CreateFileContextMenu(ExplorerItem file)
    {
        //a dummy context menu
        _contextMenu ??= new ContextMenu();
        return _contextMenu;
    }

    public override IReadOnlyList<ExplorerItem> GetItemsInFolder(string? subPath)
    {
        //no folder
        return _items;
    }

    public override void TapFile(ExplorerItem? file)
    {
        SelectedItem = file;
    }

    public override void DoubleTapFile(ExplorerItem? file)
    {
        SelectedItem = file;
    }

    public override Task<IReadOnlyList<ExplorerItem>> SearchItems(string? keyword, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<ExplorerItem>>(() =>
        {
            _searchResults.Clear();
            _searchResults.AddRange(_items.Where(item => item.Path.Contains(keyword ?? string.Empty, StringComparison.CurrentCultureIgnoreCase)));
            return _searchResults.ToArray();
        });
    }
}