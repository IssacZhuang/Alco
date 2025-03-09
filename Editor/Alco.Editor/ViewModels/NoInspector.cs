using System;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class NoInspector : Inspector
{
    public string TextHint { get; set; } = "";
    public override bool IsModified => false;

    public NoInspector(string textHint)
    {
        TextHint = textHint;
    }

    public override Control CreateControl()
    {
        return new Views.NoInspector()
        {
            DataContext = this
        };
    }

    public override Type GetAssetType(string path)
    {
        throw new NotImplementedException();
    }

    public override void OnOpenAsset(EditorEngine engine, object asset, string path)
    {
        throw new NotImplementedException();
    }

    public override void SaveAsset(EditorEngine engine)
    {
        throw new NotImplementedException();
    }
}

