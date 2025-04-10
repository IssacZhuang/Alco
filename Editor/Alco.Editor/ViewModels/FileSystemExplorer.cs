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

namespace Alco.Editor.ViewModels;

public class FileSystemExplorer : FileExplorer
{
    private static readonly (MethodInfo, ContextMenuItemAttribute)[] _contextMenuItems = UtilsAttribute.GetMethodsWithAttribute<ContextMenuItemAttribute>(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);

    private readonly List<Models.ExplorerItem> _objects = [];
    public string BasePath { get; }
    public List<TreeItem<ContextMenuItem?>> ContextMenuItemInfos { get; } = [];

    public event Action<Models.ExplorerItem?>? OnFileTapped;
    public event Action<Models.ExplorerItem?>? OnFileDoubleTapped;



    public FileSystemExplorer(string basePath)
    {
        BasePath = basePath;
        SetupContextMenu();
    }

    public override IReadOnlyList<ExplorerItem> GetItemsInFolder(string? subPath)
    {
        _objects.Clear();
        subPath ??= "";
        string path = Path.Combine(BasePath, subPath);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            string relativePath = Path.GetRelativePath(BasePath, entry);
            if (Directory.Exists(entry))
            {
                _objects.Add(new Models.ExplorerItem { Type = Models.ExplorerItemType.Folder, Path = relativePath });
            }
            else
            {
                _objects.Add(new Models.ExplorerItem { Type = Models.ExplorerItemType.File, Path = relativePath });
            }
        }
        return _objects;
    }

    public override Task<IReadOnlyList<ExplorerItem>> SearchItems(string? keyword, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<ExplorerItem>>(() =>
        {
            var result = new List<Models.ExplorerItem>();

            if (string.IsNullOrWhiteSpace(keyword))
            {
                return result;
            }

            try
            {
                // Search all files and directories recursively
                foreach (var entry in Directory.EnumerateFileSystemEntries(BasePath, "*", SearchOption.AllDirectories))
                {
                    // Check if operation was cancelled
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    string relativePath = Path.GetRelativePath(BasePath, entry);

                    // Check if the path contains the keyword (case-insensitive)
                    if (relativePath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        if (Directory.Exists(entry))
                        {
                            result.Add(new Models.ExplorerItem
                            {
                                Type = Models.ExplorerItemType.Folder,
                                Path = relativePath
                            });
                        }
                        else
                        {
                            result.Add(new Models.ExplorerItem
                            {
                                Type = Models.ExplorerItemType.File,
                                Path = relativePath
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception but return empty result
                Log.Error($"Error searching items: {ex}");
            }

            return result;
        }, cancellationToken);
    }

    public override void TapFile(Models.ExplorerItem? file)
    {
        OnFileTapped?.Invoke(file);
    }

    public override void DoubleTapFile(Models.ExplorerItem? file)
    {
        OnFileDoubleTapped?.Invoke(file);
    }

    public override ContextMenu CreateFileContextMenu(Models.ExplorerItem file)
    {
        ContextMenu menu = new ContextMenu();
        foreach (var item in ContextMenuItemInfos)
        {
            menu.Items.Add(CreateMenuItem(item, file));
        }
        return menu;
    }

    private MenuItem CreateMenuItem(TreeItem<ContextMenuItem?> item, Models.ExplorerItem file)
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

