using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Alco.Editor.Attributes;
using Alco.Editor.Models;
using Alco.Editor.Views;
using Alco.Engine;
using Alco.IO;
using Alco.Unsafe;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alco.Editor.ViewModels;

[EditorPage(order: 0)]
public class ExplorerPage : Page
{
    private static readonly (Type, InspectorAttribute)[] _inpectorMetas = UtilsAttribute.GetTypesWithAttribute<InspectorAttribute>();

    public override string IconKey => "Icons.Folder";
    public override string Tooltip => "Explorer";
    public override string Name => "Explorer";

    public override Control Control { get; }

    //empty engine only for design mode
    public ExplorerPage()
    {
        Control = new Views.ExplorerPage()
        {
            DataContext = this
        };

    }

    public unsafe Task<Inspector> OpenFile(EditorEngine engine, string filePath)
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

                    //use real project directory but not virtual file system in asset system
                    string? projectPath = engine.ProjectDirectory;

                    if (projectPath == null)
                    {
                        return new InspectorForException(
                            $"No project is open",
                            new InvalidOperationException("No project is open"));
                    }

                    string realPath = Path.Combine(projectPath, filePath);
                    byte* data = null;

                    try
                    {
                        
                        //use unmanaged memory to avoid LOH
                        data = UnsafeIO.ReadFile(realPath, out int size);

                        object asset = engine.Assets.Decode(filePath, attribute.AssetType, new ReadOnlySpan<byte>(data, size));

                        inspector.OnOpenAsset(engine, asset);
                        return inspector;
                    }
                    catch (Exception ex)
                    {
                        if (data != null)
                        {
                            UtilsMemory.Free(data);
                        }

                        return new InspectorForException(
                            $"Exception when open: {filePath}",
                            ex);
                    }
                }
            }

            NoInspector noInspector = new($"{filePath} has not inspector available");
            return noInspector;
        });

    }
}

