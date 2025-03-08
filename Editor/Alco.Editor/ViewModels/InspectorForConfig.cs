using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Alco.Editor.Attributes;
using Alco.Engine;
using Alco.IO;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

[Inspector(typeof(BaseConfig), ".json")]
public partial class InspectorForConfig : Inspector<BaseConfig>
{
    public override bool IsModified => false;
    private BaseConfig? _asset;
    private string? _serializedJson = null;

    public string SerializedJson => _serializedJson ?? "{}";
    public BaseConfig? Asset => _asset;

    public int TestNumber { get; set; } = 100;

    public InspectorForConfig()
    {
    }

    public override Control CreateControl()
    {
        return new Views.InspectorForConfig()
        {
            DataContext = this
        };
    }

    protected override void OnOpenAsset(EditorEngine engine, BaseConfig asset)
    {
        _asset = asset;
        AssetSystem assetSystem = engine.Assets;
        using SafeMemoryHandle memory = assetSystem.EncodeToBinary(_asset);
        _serializedJson = Encoding.UTF8.GetString(memory.Span);
    }
}
