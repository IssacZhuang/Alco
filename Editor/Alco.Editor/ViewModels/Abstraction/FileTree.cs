

using System.Collections.Generic;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;


public abstract class FileTree : ViewModelBase
{
    /// <summary>
    /// Get file item in path
    /// </summary>
    /// <param name="subPath">sub path of the file</param>
    /// <returns>file item list</returns>
    public abstract IReadOnlyList<Models.FileSystemItem> GetRevisionFilesUnderFolder(string? subPath);

    /// <summary>
    /// Called when a file is double clicked
    /// </summary>
    /// <param name="file">file item</param>
    public abstract void OpenFile(Models.FileSystemItem? file);

    /// <summary>
    /// Create file context menu
    /// </summary>
    /// <param name="file">file item</param>
    /// <returns>context menu</returns>
    public abstract ContextMenu CreateFileContextMenu(Models.FileSystemItem file);
}