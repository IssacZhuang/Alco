using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia;
using Alco.Editor.Models;
using System.Collections.Generic;
using Alco.Editor.ViewModels;
using System;
using Avalonia.Interactivity;

namespace Alco.Editor.Views;

public partial class Editor : Window
{
    private Page? _currentPage;
    private ViewModels.Editor? _viewModel;

    public ViewModels.Editor ViewModel => _viewModel ??= (DataContext as ViewModels.Editor)!;

    public Editor()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.Dispose();
        }
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

        if (_currentPage != null)
        {
            _currentPage.IsActive = false;
        }

        _currentPage = page;
        MainContent.Content = page.Control;
        _currentPage.IsActive = true;

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
        MainMenu.Items.Clear();

        foreach (var menuItem in ViewModel.MainMenuItems)
        {
            var topLevelMenu = new MenuItem { Header = menuItem.Header };

            foreach (var subItem in menuItem.Child)
            {
                var subMenuItem = new MenuItem { Header = subItem.Value.Header };
                subMenuItem.Click += (s, e) => subItem.Value.Action?.Invoke(this);
                topLevelMenu.Items.Add(subMenuItem);
            }

            MainMenu.Items.Add(topLevelMenu);
        }
    }
}
