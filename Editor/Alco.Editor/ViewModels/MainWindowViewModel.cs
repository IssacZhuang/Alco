using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alco.Editor.Attributes;
using Alco.Editor.Models;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels
{
    public class MenuItemViewModel
    {
        public string Header { get; set; } = string.Empty;
        public Action<Window>? Action { get; set; }
        public Dictionary<string, MenuItemViewModel> Child { get; set; } = new();
    }

    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly HashSet<Assembly> _assemblies = new();
        private readonly HashSet<string> _menuItemPaths = new();
        public string Greeting { get; } = "Welcome to Avalonia!";
        public List<Page> Pages { get; } = [];
        public List<MenuItemViewModel> MainMenuItems { get; } = [];

        public MainWindowViewModel(params Assembly[] assemblies)
        {
            _assemblies.Add(Assembly.GetExecutingAssembly());
            _assemblies.Add(Assembly.GetEntryAssembly()!);
            foreach (var assembly in assemblies)
            {
                _assemblies.Add(assembly);
            }

            SetupPages();
            SetupMainMenu();
        }

        private void SetupPages()
        {
            Pages.Add(new ExplorerPage());
        }

        private void SetupMainMenu()
        {
            var menuItemMethods = GetMenuItemMethods();
            foreach (var (method, attribute) in menuItemMethods)
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
            MenuItemViewModel? currentItem = null;

            for (int i = 0; i < path.Length; i++)
            {
                var segment = path[i];

                if (i == 0) // Top level menu
                {
                    currentItem = currentLevel.FirstOrDefault(x => x.Header == segment);
                    if (currentItem == null)
                    {
                        currentItem = new MenuItemViewModel { Header = segment };
                        currentLevel.Add(currentItem);
                    }
                }
                else // Sub menu
                {
                    if (!currentItem!.Child.TryGetValue(segment, out var childItem))
                    {
                        childItem = new MenuItemViewModel { Header = segment };
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

        private IEnumerable<(MethodInfo, MenuItemAttribute)> GetMenuItemMethods()
        {
            return from assembly in _assemblies
                   from type in assembly.GetTypes()
                   from method in type.GetMethods()
                   where method.GetCustomAttributes(typeof(MenuItemAttribute), false).Length > 0
                   select (method, method.GetCustomAttribute<MenuItemAttribute>()!);
        }
    }
}
