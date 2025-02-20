using System;
using System.IO;
using System.Threading.Tasks;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.Models;

public static class ExplorerContextMenuItems
{
    [ContextMenuItem("Create Folder")]
    public static void CreateFolder(EditorEngine engine, string path)
    {
        Log.Success(path);
    }

    [ContextMenuItem("Delete")]
    public static void Delete(EditorEngine engine, string path)
    {
        Log.Success(path);
    }

    [ContextMenuItem("Rename")]
    public static void Rename(EditorEngine engine, string path)
    {
        Log.Success(path);
    }
    
}
