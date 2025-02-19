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
public partial class ConfigInspector : Inspector<BaseConfig>
{
    public override bool IsModified => false;
    private BaseConfig? _asset;
    private string? _serializedJson = null;

    public string SerializedJson => _serializedJson ?? "{}";

    public ConfigInspector()
    {
    }

    public override Control CreateControl()
    {
        return new Views.ConfigInspector()
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
