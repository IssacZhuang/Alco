using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia;
using Alco.Editor.Models;
using System.Collections.Generic;

namespace Alco.Editor.Views
{
    public partial class MainWindow : Window
    {
     
        private readonly List<Page> _pages;
        private Page? _currentPage;

        public MainWindow()
        {
            InitializeComponent();
            InitializeMenu();

            _pages = new List<Page>
            {
                new ExplorerPage(),
            };

            InitializeActivityBar();

            if (_pages.Count > 0)
            {
                SwitchToPage(_pages[0]);
            }
        }

        private void InitializeActivityBar()
        {
            foreach (var page in _pages)
            {
                var button = new Button
                {
                    Width = 48,
                    Height = 48,
                    Margin = new Thickness(0, 5),
                    Background = Brushes.Transparent
                };

                ToolTip.SetTip(button, page.Tooltip);

                var pathIcon = new PathIcon
                {
                    Data = page.IconGeometry,
                    Foreground = new SolidColorBrush(Color.Parse("#D1D1D1"))
                };

                button.Content = pathIcon;
                button.Click += (s, e) => SwitchToPage(page);
                ActivityBar.Children.Add(button);
            }
        }

        private void SwitchToPage(Page page)
        {
            if (_currentPage == page) return;

            _currentPage?.OnDeactivated();
            _currentPage = page;
            MainContent.Content = page.Content;
            _currentPage.OnActivated();

            foreach (Button button in ActivityBar.Children)
            {
                var pathIcon = button.Content as PathIcon;
                if (pathIcon != null)
                {
                    if (ToolTip.GetTip(button)?.ToString() == page.Tooltip)
                    {
                        pathIcon.Foreground = new SolidColorBrush(Color.Parse("#FFFFFF"));
                        button.Background = new SolidColorBrush(Color.Parse("#404040"));
                    }
                    else
                    {
                        pathIcon.Foreground = new SolidColorBrush(Color.Parse("#D1D1D1"));
                        button.Background = Brushes.Transparent;
                    }
                }
            }
        }

        private void InitializeMenu()
        {
            // File Menu
            var fileMenu = new MenuItem { Header = "_File" };
            fileMenu.Items.Add(new MenuItem { Header = "_New" });
            fileMenu.Items.Add(new MenuItem { Header = "_Open..." });
            fileMenu.Items.Add(new MenuItem { Header = "_Save" });
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(new MenuItem { Header = "_Exit" });

            // Edit Menu
            var editMenu = new MenuItem { Header = "_Edit" };
            editMenu.Items.Add(new MenuItem { Header = "_Undo" });
            editMenu.Items.Add(new MenuItem { Header = "_Redo" });
            editMenu.Items.Add(new Separator());
            editMenu.Items.Add(new MenuItem { Header = "Cu_t" });
            editMenu.Items.Add(new MenuItem { Header = "_Copy" });
            editMenu.Items.Add(new MenuItem { Header = "_Paste" });

            // View Menu
            var viewMenu = new MenuItem { Header = "_View" };
            viewMenu.Items.Add(new MenuItem { Header = "_Explorer" });
            viewMenu.Items.Add(new MenuItem { Header = "_Search" });
            viewMenu.Items.Add(new MenuItem { Header = "_Extensions" });

            // Add all menus to the main menu
            MainMenu.Items.Add(fileMenu);
            MainMenu.Items.Add(editMenu);
            MainMenu.Items.Add(viewMenu);
        }
    }
}