using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alco.Editor.Attributes;
using Alco.Engine;
using Avalonia.Controls;
using Alco.Editor.Models;
namespace Alco.Editor.ViewModels;



public partial class Editor : ViewModelBase, IDisposable
{
    private static readonly (MethodInfo, MenuItemAttribute)[] _menuItemMethods = UtilsAttribute.GetMethodsWithAttribute<MenuItemAttribute>(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
    private static readonly (Type, EditorPageAttribute)[] _editorPages = UtilsAttribute.GetTypesWithAttribute<EditorPageAttribute>(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);

    private bool _disposed;
    private readonly HashSet<string> _menuItemPaths = new();

    public GameEngine Engine { get; }
    public List<Page> Pages { get; } = [];
    public List<MenuItemInfo> MainMenuItems { get; } = [];

    public Editor()
    {
        if (!Design.IsDesignMode)
        {
            Engine = new GameEngine(GameEngineSetting.CreateGPUWithoutWindow());
        }
        else
        {
            // the engine is not init in the design mode
            Engine = null!;
        }

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

                var page = Activator.CreateInstance(type) as Page;

                Pages.Add(page!);
            }
            catch (Exception ex)
            {
                Log.Error($"Error creating page {type.Name}: {ex.Message}");
            }
        }
    }

    private void SetupMainMenu()
    {
        foreach (var (method, attribute) in _menuItemMethods)
        {
            AddMenuItem(method, attribute);
        }
    }

    private void AddMenuItem(MethodInfo method, MenuItemAttribute attribute)
    {
        if (_menuItemPaths.Contains(attribute.Path))
        {
            Console.WriteLine($"Duplicate menu item path: {attribute.Path}");
            return;
        }

        _menuItemPaths.Add(attribute.Path);

        string[] path = attribute.Path.Split('/');

        var currentLevel = MainMenuItems;
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
                currentItem.Action = (window) => method.Invoke(null, new[] { window });
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            Engine.Dispose();
        }

        _disposed = true;
    }

    ~Editor()
    {
        Dispose(false);
    }
}

