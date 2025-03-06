using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alco.Editor.Attributes;
using Alco.Engine;
using Avalonia.Controls;
using Alco.Editor.Models;
namespace Alco.Editor.ViewModels;



public partial class Editor : ViewModelBase
{
    private static readonly (MethodInfo, MenuItemAttribute)[] _menuItemMethods = UtilsAttribute.GetMethodsWithAttribute<MenuItemAttribute>(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
    private static readonly (Type, EditorPageAttribute)[] _editorPages = UtilsAttribute.GetTypesWithAttribute<EditorPageAttribute>();
    
    public List<Page> Pages { get; } = [];
    public List<TreeItem<MethodInfo?>> MainMenuItems { get; } = [];

    public Editor()
    {
        SetupPages();
        SetupMainMenu();
    }

    private void SetupPages()
    {
        foreach (var (type, attribute) in _editorPages)
        {
            try
            {
                if (!type.IsAssignableTo(typeof(Page)))
                {
                    Log.Error($"Failed to create page {type.Name}, because it is not assignable to {typeof(Page).Name}");
                    continue;
                }

                Page page = (Page)Activator.CreateInstance(type)!;
                
                Pages.Add(page);
            }
            catch (Exception ex)
            {
                Log.Error($"Error creating page {type.Name}: {ex}");
            }
        }
    }

    private void SetupMainMenu()
    {
        foreach (var (method, attribute) in _menuItemMethods)
        {
            MainMenuItems.AddTreeItem(attribute.Path, method);
        }
    }

}

