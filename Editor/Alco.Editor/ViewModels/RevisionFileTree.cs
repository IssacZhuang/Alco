

using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class RevisionFileTree : ViewModelBase
{
    private List<Models.Object> _objects = [];
    public string BasePath { get; }

    public RevisionFileTree(string basePath)
    {
        BasePath = basePath;
    }

    public void RefreshDirectory()
    {
        _objects.Clear();
    }

    public List<Models.Object> GetRevisionFilesUnderFolder(string? subPath)
    {
        subPath = subPath ?? "";
        string path = Path.Combine(BasePath, subPath);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (Directory.Exists(entry))
            {
                _objects.Add(new Models.Object { Type = Models.ObjectType.Tree, Path = entry });
            }
            else
            {
                _objects.Add(new Models.Object { Type = Models.ObjectType.Blob, Path = entry });
            }
        }
        return _objects;
    }

    public void ViewRevisionFile(Models.Object? file)
    {

    }

    public ContextMenu CreateRevisionFileContextMenu(Models.Object file)
    {
        return new ContextMenu();
    }
}

