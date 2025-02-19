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

    public EditorEngine Engine { get; }
    public List<Page> Pages { get; } = [];
    public List<TreeItem<MethodInfo?>> MainMenuItems { get; } = [];

    public Editor()
    {
        if (!Design.IsDesignMode)
        {
            Engine = new EditorEngine(GameEngineSetting.CreateGPUWithoutWindow());
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

                Page page;

                //the type has a constructor with EditorEngine parameter
                var constructor = type.GetConstructor([typeof(EditorEngine)]);
                if (constructor != null)
                {
                    page = (Page)Activator.CreateInstance(type, Engine)!;
                }
                else
                {
                    page = (Page)Activator.CreateInstance(type)!;
                }

                Pages.Add(page);
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
            MainMenuItems.AddTreeItem(attribute.Path, method);
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

