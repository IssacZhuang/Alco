using System.Threading.Tasks;
using Alco.Editor.Attributes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;


namespace Alco.Editor.Models;

public static class DefaultMenuItems
{
    [MenuItem("File/Open Project", 0)]
    public static async ValueTask OpenProject(Window window)
    {
        await App.Main.ShowOpenProjectDialog();
    }

    [MenuItem("File/Close Project", 1)]
    public static void OpenTest(Window window)
    {
        App.Main.Engine.CloseProject();
    }

    [MenuItem("File/Exit", 2)]
    public static void Exit(Window window)
    {
        window.Close();
    }


}
