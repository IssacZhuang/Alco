using Alco.Editor.Attributes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;


namespace Alco.Editor.Models;

public static class DefaultMenuItems
{
    [MenuItem("File/Exit")]
    public static void Exit(Window window)
    {
        window.Close();
    }

    [MenuItem("File/Open Project")]
    public static void OpenProject(Window window)
    {
        
    }

    [MenuItem("File/Test/Test")]
    public static void OpenTest(Window window)
    {

    }



}
