using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Alco.Editor.Attributes;
using Alco.Editor.Models;
using Alco.Editor.Views;
using Alco.Engine;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alco.Editor.ViewModels;

[EditorPage]
public class ExplorerPage : Page
{
    private static readonly (MethodInfo, ContextMenuItemAttribute)[] _contextMenuItems = UtilsAttribute.GetMethodsWithAttribute<ContextMenuItemAttribute>(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);


    private static readonly (Type, InspectorAttribute)[] _inpectorMetas = UtilsAttribute.GetTypesWithAttribute<InspectorAttribute>(BindingFlags.Public | BindingFlags.Instance);

    public override string IconData => "M903.253 231.253V682.24L855.467 736H679.253v170.24L625.493 960H174.507l-53.76-53.76V344.747L174.507 288h170.24V120.747L398.507 64H736l167.253 167.253z m-167.253 0h89.6l-89.6-89.6v89.6zM622.507 736h-224l-53.76-53.76V344.747h-170.24v558.507h448V736z m224-448H679.253V120.747H398.507v558.507h448V288z";
    public override string Tooltip => "Explorer";
    public override string Name => "Explorer";

    public override Control Control { get; }


    public List<TreeItem<MethodInfo?>> ContextMenuItemInfos { get; } = [];

    public List<TreeItem<string>> FileNames { get; } = [];
    //empty engine only for design mode

    //you should be use this constructor in runtime
    public ExplorerPage()
    {
        Control = new Views.ExplorerPage()
        {
            DataContext = this
        };

        SetupContextMenu();
    }


    public async Task OpenProject(EditorEngine engine, string projectPath)
    {
        engine.OpenProject(projectPath);
        await RefreshFileNames(engine);
    }

    private void SetupContextMenu()
    {
        ContextMenuItemInfos.Clear();
        foreach (var (method, attribute) in _contextMenuItems)
        {
            ContextMenuItemInfos.AddTreeItem(attribute.Path, method);
        }
    }

    private Task RefreshFileNames(EditorEngine engine)
    {
        return Task.Run(() =>
        {
            FileNames.Clear();
            string[] allAssets = engine.Assets.AllFileNames.ToArray();
            foreach (var asset in allAssets)
            {
                FileNames.AddTreeItem(asset, asset);
            }
        });
    }

    public Task<Inspector> OpenFile(EditorEngine engine, string filePath)
    {
        return Task.Run(() =>
        {
            string extension = Path.GetExtension(filePath);
            foreach (var (type, attribute) in _inpectorMetas)
            {
                if (attribute.IsSupported(extension))
                {
                    if (Activator.CreateInstance(type) is not Inspector inspector)
                    {
                        continue;
                    }

                    object asset = engine.Assets.Load(filePath, type);

                    inspector.OnOpenAsset(asset);
                    return inspector;
                }
            }

            NoInspector noInspector = new($"{filePath} has not inspector available");
            return noInspector;
        });

    }
}

