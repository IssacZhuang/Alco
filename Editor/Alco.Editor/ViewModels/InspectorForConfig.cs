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

    public string SerializedJson
    {
        get => _serializedJson ?? "{}";
        set
        {
            _serializedJson = value;
            OnPropertyChanged(nameof(SerializedJson));
        }
    }
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

    public void RefreshSerializedJson(EditorEngine engine)
    {
        AssetSystem assetSystem = engine.Assets;
        using SafeMemoryHandle memory = assetSystem.EncodeToBinary(_asset);
        SerializedJson = Encoding.UTF8.GetString(memory.Span);
    }

    public IEnumerable<string> Validate(EditorEngine engine)
    {
        if (_asset is IValidatableConfig validatableConfig)
        {
            return validatableConfig.Validate(engine);
        }
        return [];
    }

    protected override void OnOpenAsset(EditorEngine engine, BaseConfig asset)
    {
        _asset = asset;
        AssetSystem assetSystem = engine.Assets;
        using SafeMemoryHandle memory = assetSystem.EncodeToBinary(_asset);
        SerializedJson = Encoding.UTF8.GetString(memory.Span);
    }
}
