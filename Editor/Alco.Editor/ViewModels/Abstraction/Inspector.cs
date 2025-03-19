using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;

namespace Alco.Editor.ViewModels;

public abstract class Inspector : ViewModelBase
{
    public abstract bool IsModified { get; }

    public abstract Control CreateControl();

    public abstract Type GetAssetType(string path);
    public abstract void OnOpenAsset(EditorEngine engine, object asset, string path);
    public abstract void SaveAsset(EditorEngine engine);
}

public abstract class Inspector<T> : Inspector
{
    public override Type GetAssetType(string path)
    {
        return typeof(T);
    }

    public override void OnOpenAsset(EditorEngine engine, object asset, string path)
    {
        OnOpenAsset(engine, (T)asset, path);
    }

    protected abstract void OnOpenAsset(EditorEngine engine, T asset, string path);
}
