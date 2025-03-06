

using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class FileSystemTree : FileTree
{
    private List<Models.FileSystemItem> _objects = [];
    public string BasePath { get; }

    public FileSystemTree(string basePath)
    {
        BasePath = basePath;
    }

    public override IReadOnlyList<Models.FileSystemItem> GetRevisionFilesUnderFolder(string? subPath)
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
        menu.Items.Add(new MenuItem { Header = "Open",});
        return menu;
    }
}

