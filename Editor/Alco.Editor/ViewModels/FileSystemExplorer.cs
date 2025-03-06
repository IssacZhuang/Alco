

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Alco.Editor.Attributes;
using Alco.Editor.Models;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class FileSystemExplorer : FileExplorer
{
    private static readonly (MethodInfo, ContextMenuItemAttribute)[] _contextMenuItems = UtilsAttribute.GetMethodsWithAttribute<ContextMenuItemAttribute>(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);

    private readonly List<Models.FileSystemItem> _objects = [];
    public string BasePath { get; }
    public List<TreeItem<ContextMenuItem?>> ContextMenuItemInfos { get; } = [];

    public FileSystemExplorer(string basePath)
    {
        BasePath = basePath;
        SetupContextMenu();
    }

    public override IReadOnlyList<Models.FileSystemItem> GetItemsInFolder(string? subPath)
    {
        _objects.Clear();
        subPath ??= "";
        string path = Path.Combine(BasePath, subPath);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            string relativePath = Path.GetRelativePath(BasePath, entry);
            if (Directory.Exists(entry))
            {
                _objects.Add(new Models.FileSystemItem { Type = Models.FileSystemItemType.File, Path = relativePath });
            }
            else
            {
                _objects.Add(new Models.FileSystemItem { Type = Models.FileSystemItemType.Folder, Path = relativePath });
            }
        }
        return _objects;
    }

    public override void OpenFile(Models.FileSystemItem? file)
    {

    }

    public override ContextMenu CreateFileContextMenu(Models.FileSystemItem file)
    {
        ContextMenu menu = new ContextMenu();
        foreach (var item in ContextMenuItemInfos)
        {
            menu.Items.Add(CreateMenuItem(item, file));
        }
        return menu;
    }

    private MenuItem CreateMenuItem(TreeItem<ContextMenuItem?> item, Models.FileSystemItem file)
    {
        var menuItem = new MenuItem{Header = item.Header};
        foreach (var child in item.Child)
        {
            menuItem.Items.Add(CreateMenuItem(child.Value, file));
        }

        if (item.UserData != null)
        {
            menuItem.Click += (_, _) => item.UserData(file.Path);
        }
        return menuItem;
    }

    private void SetupContextMenu()
    {
        ContextMenuItemInfos.Clear();
        foreach (var (method, attribute) in _contextMenuItems)
        {
            try
            {
                ContextMenuItemInfos.AddTreeItem(attribute.Path, method.CreateDelegate<ContextMenuItem>());
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to create context menu item: {ex}");
            }
        }
    }


}

