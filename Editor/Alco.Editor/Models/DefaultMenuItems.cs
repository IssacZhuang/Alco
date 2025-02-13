using Alco.Editor.Attributes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using static System.Net.Mime.MediaTypeNames;

namespace Alco.Editor.Models
{
    public static class DefaultMenuItems
    {
        [MenuItem("File/Exit")]
        public static void Exit(Window window)
        {
            window.Close();
        }


    }
}