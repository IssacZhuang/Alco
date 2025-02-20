using System;
using System.IO;
using System.Threading.Tasks;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.Models;

public static class ExplorerContextMenuItems
{
    [ContextMenuItem("Create Folder")]
    public static void CreateFolder(EditorEngine engine, string localPath)
    {
        if (engine.ProjectDirectory == null)
        {
            return;
        }

        string fullPath = Path.Combine(engine.ProjectDirectory, localPath);
        try
        {
            string? targetDirectory;
            if (Directory.Exists(fullPath))
            {
                // If path is a directory, create folder inside it
                targetDirectory = fullPath;
            }
            else if (File.Exists(fullPath))
            {
                // If path is a file, get its directory
                targetDirectory = Path.GetDirectoryName(fullPath);
            }
            else
            {
                Log.Warning($"Path does not exist: {fullPath}");
                return;
            }

            if (targetDirectory == null)
            {
                Log.Error($"Invalid path: {fullPath}");
                return;
            }
            

            string newFolderName = "New Folder";
            string newFolderPath = Path.Combine(targetDirectory, newFolderName);

            // Handle duplicate names
            int counter = 1;
            while (Directory.Exists(newFolderPath))
            {
                newFolderName = $"New Folder ({counter})";
                newFolderPath = Path.Combine(targetDirectory, newFolderName);
                counter++;
            }

            Directory.CreateDirectory(newFolderPath);
            Log.Success($"Created folder: {newFolderPath}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to create folder: {ex.Message}");
        }
    }

    [ContextMenuItem("Delete")]
    public static void Delete(EditorEngine engine, string path)
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
    public static void Rename(EditorEngine engine, string path)
    {
        Log.Success(path);
    }
    
}
