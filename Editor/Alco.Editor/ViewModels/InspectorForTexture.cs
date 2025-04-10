using System;
using System.Collections.Generic;
using Alco.Editor.Attributes;
using Alco.Engine;
using Alco.IO;
using Alco.Rendering;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

[Inspector(typeof(Texture2D), FileExt.ImagePNG, FileExt.ImageJPG, FileExt.ImageBMP, FileExt.ImageTGA, FileExt.ImageGIF, FileExt.ImageHDR)]
public class InspectorForTexture : Inspector<Texture2D>
{
    private bool _isModified = false;
    private Texture2D? _asset;
    private string? _filename = null;
    private string? _path;

    public override bool IsModified => _isModified;

    public string Filename
    {
        get => _filename ?? "Untitled";
    }

    public Texture2D? Asset => _asset;

    public InspectorForTexture()
    {
    }

    public override Control CreateControl()
    {
        return new Views.InspectorForTexture()
        {
            DataContext = this
        };
    }

    protected override void OnOpenAsset(EditorEngine engine, Texture2D asset, string path)
    {
        _asset = asset;
        _path = path;
        _filename = System.IO.Path.GetFileName(path);
        _isModified = false;
    }

    public override void SaveAsset(EditorEngine engine)
    {
        if (_asset == null || _path == null)
        {
            return;
        }

        // For textures, since we're only displaying and not editing in this implementation,
        // there's typically no saving needed. If texture editing is implemented later,
        // this method would need to be expanded.
        _isModified = false;
    }
}