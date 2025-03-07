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

namespace Alco.Editor.ViewModels;

public class ConfigTypeExplorer : FileExplorer
{
    private readonly List<ExplorerItem> _items = [];
    private readonly List<ExplorerItem> _searchResults = [];
    private ContextMenu? _contextMenu;
    public ConfigTypeExplorer(params Type[] types)
    {
        _items.AddRange(types.Select(t => new ExplorerItem
        {
            Type = ExplorerItemType.Type,
            Path = t.FullName ?? string.Empty,
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

    public override void OpenFile(ExplorerItem? file)
    {
        //do nothing
    }

    public override Task<IReadOnlyList<ExplorerItem>> SearchItems(string? keyword, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<ExplorerItem>>(() =>
        {
            _searchResults.Clear();
            _searchResults.AddRange(_items.Where(item => item.Path.Contains(keyword ?? string.Empty)));
            return _searchResults.ToArray();
        });
    }
}