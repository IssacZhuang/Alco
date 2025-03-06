using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using System.Collections.Generic;

using Alco.Editor.Models;
using Alco.Editor.Attributes;
using Alco.Editor.Utility;
using Avalonia;
using Avalonia.LogicalTree;


namespace Alco.Editor.Views;

public partial class ExplorerPage : UserControl
{
    
    private ContextMenu? _contextMenu;
    public ViewModels.ExplorerPage ViewModel => DataContext as ViewModels.ExplorerPage ?? throw new InvalidOperationException("DataContext is not a ViewModels.ExplorerPage");

    public ExplorerPage()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        if (!Design.IsDesignMode)
        {
            OnRefreshProjectFiles();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (!Design.IsDesignMode)
        {
            EditorEngine engine = App.Main.Engine;
            engine.OnFilesInProjectUpdated += OnRefreshProjectFiles;
            engine.OnProjectOpened += OnProjectOpened;
            engine.OnProjectClosed += OnProjectClosed;

            FileTreeView.IsVisible = engine.IsProjectOpen;
            NoFolderPanel.IsVisible = !engine.IsProjectOpen;
        }
        base.OnAttachedToVisualTree(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (!Design.IsDesignMode)
        {
            EditorEngine engine = App.Main.Engine;
            engine.OnFilesInProjectUpdated -= OnRefreshProjectFiles;
            engine.OnProjectOpened -= OnProjectOpened;
            engine.OnProjectClosed -= OnProjectClosed;
        }
        base.OnDetachedFromVisualTree(e);
    }


    private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        await App.Main.ShowOpenProjectDialog();
    }

    private void OnRefreshProjectFiles()
    {
        if (DataContext is not ViewModels.ExplorerPage viewModel)
        {
            throw new InvalidOperationException("DataContext is not a ViewModels.ExplorerPage");
        }

        EditorEngine engine = App.Main.Engine;
        string? projectDir = engine.ProjectDirectory;
        if (projectDir == null)
        {
            //project is not open
            return;
        }

        ViewModels.FileExplorer vmFileTree = new ViewModels.FileSystemExplorer(projectDir);
        FileTreeView.DataContext = vmFileTree;
        //FileTreeView.SetSearchResult(null);
    }
    private void OnProjectOpened()
    {
        EditorEngine engine = App.Main.Engine;
        OnRefreshProjectFiles();
        FileTreeView.IsVisible = engine.IsProjectOpen;
        NoFolderPanel.IsVisible = !engine.IsProjectOpen;

        
    }

    private void OnProjectClosed()
    {
        FileTreeView.IsVisible = false;
        NoFolderPanel.IsVisible = true;
    }






}
