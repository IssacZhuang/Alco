using System;
using System.IO;
using System.Threading.Tasks;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.Models;

public static class ExplorerContextMenuItems
{
    [ContextMenuItem("Create Folder")]
    public static void CreateFolder(string localPath)
    {
        EditorEngine engine = App.Main.Engine;
        Views.Editor? editorWindow = App.Main.EditorWindow;
        if (editorWindow == null)
        {
            return;
        }

        if (engine.ProjectDirectory == null)
        {
            return;
        }

        string fullPath = Path.Combine(engine.ProjectDirectory, localPath);
        ViewModels.CreateFolderDialog viewModel = new ViewModels.CreateFolderDialog(fullPath);
        Views.CreateFolderDialog window = viewModel.CreateWindow();
        window.ShowDialog(editorWindow);
    }

    [ContextMenuItem("Delete")]
    public static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                Log.Success($"Deleted folder: {path}");
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
                Log.Success($"Deleted file: {path}");
            }
            else
            {
                Log.Warning($"Path does not exist: {path}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to delete: {ex.Message}");
        }
    }

    [ContextMenuItem("Rename")]
    public static void Rename(string path)
    {
        Log.Success(path);
    }
    
}
