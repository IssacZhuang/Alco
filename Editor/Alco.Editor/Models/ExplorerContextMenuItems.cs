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

        if (engine.ProjectDirectory == null)
        {
            return;
        }

        string fullPath = Path.Combine(engine.ProjectDirectory, localPath);
        ViewModels.CreateFolderDialog viewModel = new ViewModels.CreateFolderDialog(fullPath);
        Views.CreateFolderDialog window = viewModel.CreateWindow();
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
