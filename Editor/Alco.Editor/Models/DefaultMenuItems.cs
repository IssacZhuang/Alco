using System.Threading.Tasks;
using Alco.Editor.Attributes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;


namespace Alco.Editor.Models;

public static class DefaultMenuItems
{
    [MenuItem("File/Close Project")]
    public static void OpenTest(Window window)
    {
        App.Main.Engine.CloseProject();
    }

    [MenuItem("File/Exit")]
    public static void Exit(Window window)
    {
        window.Close();
    }

    [MenuItem("File/Open Project")]
    public static async ValueTask OpenProject(Window window)
    {
        await App.Main.ShowOpenProjectDialog();
    }
}
