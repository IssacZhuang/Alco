using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia;
using Alco.Editor.Models;
using System.Collections.Generic;
using Alco.Editor.ViewModels;
using System;

namespace Alco.Editor.Views
{
    public partial class MainWindow : Window
    {
        private Page? _currentPage;
        private MainWindowViewModel? _viewModel;

        public MainWindowViewModel ViewModel => _viewModel ??= (DataContext as MainWindowViewModel)!;

        public MainWindow()
        {
            InitializeComponent();
            
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            SetupMenu();
            SetupActivityBar();

            if (ViewModel.Pages.Count > 0)
            {
                SwitchToPage(ViewModel.Pages[0]);
            }
        }

        private void SetupActivityBar()
        {
            //clear all children
            ActivityBar.Children.Clear();

            foreach (var page in ViewModel.Pages)
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

        private void SetupMenu()
        {
            //clear all children
            MainMenu.Items.Clear();

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