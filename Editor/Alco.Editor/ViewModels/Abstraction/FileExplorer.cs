

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alco.Editor.Models;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;


public abstract class FileExplorer : ViewModelBase
{
    public ExplorerItem? SelectedItem { get; set; }

    /// <summary>
    /// Get file item in path
    /// </summary>
    /// <param name="subPath">sub path of the file</param>
    /// <returns>file item list</returns>
    public abstract IReadOnlyList<Models.ExplorerItem> GetItemsInFolder(string? subPath);

    /// <summary>
    /// Search items in the file explorer
    /// </summary>
    /// <param name="keyword">search keyword</param>
    /// <returns>search result</returns>
    public abstract Task<IReadOnlyList<Models.ExplorerItem>> SearchItems(string? keyword, CancellationToken cancellationToken);

    /// <summary>
    /// Called when a file is double clicked
    /// </summary>
    /// <param name="file">file item</param>
    public abstract void OpenFile(Models.ExplorerItem? file);

    /// <summary>
    /// Create file context menu
    /// </summary>
    /// <param name="file">file item</param>
    /// <returns>context menu</returns>
    public abstract ContextMenu CreateFileContextMenu(Models.ExplorerItem file);
}