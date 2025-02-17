using System;
using System.IO;
using System.Threading.Tasks;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.Models
{
    public static class ExplorerContextMenuItems
    {
        [ContextMenuItem("Create/Folder")]
        public static void CreateFolder(TreeViewItem item)
        {
            Log.Info("CreateFolder");
        }
    }
}