using System.Collections.Generic;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Alco.Editor.ViewModels;

public class FileTreeNode : ObservableObject
{
    public Models.FileSystemItem? Backend { get; set; } = null;
    public int Depth { get; set; } = 0;
    public List<FileTreeNode> Children { get; set; } = new List<FileTreeNode>();

    public string Name
    {
        get => Backend == null ? string.Empty : Path.GetFileName(Backend.Path);
    }

    public bool IsFolder
    {
        get => Backend != null && Backend.Type == Models.FileSystemItemType.File;
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private bool _isExpanded = false;
}

