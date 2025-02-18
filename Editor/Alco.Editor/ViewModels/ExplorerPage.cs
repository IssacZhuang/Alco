using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Alco.Editor.Attributes;
using Alco.Editor.Models;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alco.Editor.ViewModels;

[EditorPage]
public class ExplorerPage : Page
{
    private static readonly (MethodInfo, ContextMenuItemAttribute)[] _contextMenuItems = UtilsAttribute.GetMethodsWithAttribute<ContextMenuItemAttribute>(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);

    public override string IconData => "M903.253 231.253V682.24L855.467 736H679.253v170.24L625.493 960H174.507l-53.76-53.76V344.747L174.507 288h170.24V120.747L398.507 64H736l167.253 167.253z m-167.253 0h89.6l-89.6-89.6v89.6zM622.507 736h-224l-53.76-53.76V344.747h-170.24v558.507h448V736z m224-448H679.253V120.747H398.507v558.507h448V288z";
    public override string Tooltip => "Explorer";
    public override string Name => "Explorer";

    public override Control Control { get; }

    public List<MenuItemInfo> ContextMenuItemInfos { get; } = [];

    public ExplorerPage()
    {
        Control = new Views.ExplorerPage()
        {
            DataContext = this
        };

        SetupContextMenu();
    }


    private void SetupContextMenu()
    {
        foreach (var (method, attribute) in _contextMenuItems)
        {
            AddContextMenuItem(method, attribute);
        }
    }

    private void AddContextMenuItem(MethodInfo method, ContextMenuItemAttribute attribute)
    {
        string[] path = attribute.Path.Split('/');

        var currentLevel = ContextMenuItemInfos;
        MenuItemInfo? currentItem = null;

        for (int i = 0; i < path.Length; i++)
        {
            var segment = path[i];

            if (i == 0) // Top level menu
            {
                currentItem = currentLevel.FirstOrDefault(x => x.Header == segment);
                if (currentItem == null)
                {
                    currentItem = new MenuItemInfo { Header = segment };
                    currentLevel.Add(currentItem);
                }
            }
            else // Sub menu
            {
                if (!currentItem!.Child.TryGetValue(segment, out var childItem))
                {
                    childItem = new MenuItemInfo { Header = segment };
                    currentItem.Child[segment] = childItem;
                }
                currentItem = childItem;
            }

            // Set action for the last segment
            if (i == path.Length - 1)
            {
                currentItem.Action = (item) => method.Invoke(null, new[] { item });
            }
        }
    }
}

