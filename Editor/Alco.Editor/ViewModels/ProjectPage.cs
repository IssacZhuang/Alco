using System;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

[EditorPage(order: 10)]
public class ProjectPage : Page
{
    public override Control Control { get; }

    public override string Name => "Project";

    public override string IconKey => "Icons.Settings";

    public override string Tooltip => "Project Settings";

    public ProjectPage()
    {
        Control = new Views.ProjectPage()
        {
            DataContext = this
        };
    }
}
