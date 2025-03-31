using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using Alco.Editor.Attributes;
using Avalonia.Controls;
using System.Linq;
using Alco.IO;

namespace Alco.Editor.Models;

public static class ExplorerContextMenuItems
{
    [ContextMenuItem("Create/Create Config")]
    public static void CreateConfig(string localPath)
    {
        var engine = App.Main.Engine;
        var types = App.Main.TypeDatabase.ConfigTypes;
        var dialog = new ViewModels.CreateConfigDialog(types.ToArray());

        if (engine.ProjectDirectory == null)
        {
            return;
        }


        string path = Path.Combine(engine.ProjectDirectory, localPath);
        path = File.Exists(path) ? Path.GetDirectoryName(path) ?? string.Empty : path;

        dialog.OnTypeConfirmed += (filename, type) =>
        {
            Configable? instance = Activator.CreateInstance(type) as Configable ?? throw new Exception($"The type {type.Name} is not a valid config type.");
            SafeMemoryHandle handle = engine.Assets.EncodeToBinary(instance);
            path = Path.Combine(path, filename + ".json");
            File.WriteAllBytes(path, handle.Span);
        };
        var window = dialog.CreateControl();
        ShowDialog(window);
    }

    [ContextMenuItem("Create Folder")]
    public static void CreateFolder(string localPath)
    {
        EditorEngine engine = App.Main.Engine;

        if (engine.ProjectDirectory == null)
        {
            return;
        }

        string fullPath = Path.Combine(engine.ProjectDirectory, localPath);
        ViewModels.CreateFolderDialog viewModel = new ViewModels.CreateFolderDialog(fullPath);
        Views.CreateFolderDialog window = viewModel.CreateWindow();
        ShowDialog(window);
    }



    [ContextMenuItem("Open in Explorer")]
    public static void OpenInExplorer(string path)
    {
        EditorEngine engine = App.Main.Engine;

        if (engine.ProjectDirectory == null)
        {
            return;
        }

        string fullPath = Path.Combine(engine.ProjectDirectory, path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (Directory.Exists(fullPath))
                {
                    Process.Start("open", fullPath);
                }
                else
                {
                    // On macOS, we use -R to reveal the file in Finder
                    Process.Start("open", $"-R \"{fullPath}\"");
                }
            }
        }
        catch (Exception ex)
        {
            // Handle any errors silently
            Log.Error($"Failed to open file explorer: {ex.Message}");
        }
    }

    [ContextMenuItem("Rename")]
    public static void Rename(string path)
    {
        EditorEngine engine = App.Main.Engine;

        if (engine.ProjectDirectory == null)
        {
            return;
        }

        string fullPath = Path.Combine(engine.ProjectDirectory, path);
        ViewModels.RenameDialog viewModel = new ViewModels.RenameDialog(fullPath);
        Views.RenameDialog window = viewModel.CreateWindow();
        ShowDialog(window);
    }

    [ContextMenuItem("Delete")]
    public static void Delete(string path)
    {
        EditorEngine engine = App.Main.Engine;

        if (engine.ProjectDirectory == null)
        {
            return;
        }

        string fullPath = Path.Combine(engine.ProjectDirectory, path);
        string message = $"Delete: '{path}'?";
        ViewModels.ConfirmDialog viewModel = new ViewModels.ConfirmDialog(message);

        viewModel.OnConfirm += () =>
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            else if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }
        };

        Views.ConfirmDialog window = viewModel.CreateWindow();
        ShowDialog(window);
    }

    private static void ShowDialog(Window window)
    {
        Views.Editor? editorWindow = App.Main.EditorWindow;
        if (editorWindow == null)
        {
            return;
        }

        window.ShowDialog(editorWindow);
    }
}
