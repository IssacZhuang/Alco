using System;
using System.Collections.Generic;
using System.IO;
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
    private bool _isModified = false;
    private BaseConfig? _asset;
    private string? _serializedJson = null;
    private string? _filename = null;
    private string? _path;

    public override bool IsModified => _isModified;
    public ViewModels.ObjectPropertiesEditor? PropertiesEditor { get; private set; }

    public string SerializedJson
    {
        get => _serializedJson ?? "{}";
        set
        {
            _serializedJson = value;
            OnPropertyChanged(nameof(SerializedJson));
        }
    }

    public string Filename
    {
        get => _filename ?? "Untitled";
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
            return validatableConfig.ValidateSafely(engine);
        }
        return [];
    }

    protected override void OnOpenAsset(EditorEngine engine, BaseConfig asset, string path)
    {
        _asset = asset;
        _path = path;
        _filename = Path.GetFileName(path);
        AssetSystem assetSystem = engine.Assets;
        using SafeMemoryHandle memory = assetSystem.EncodeToBinary(_asset);
        SerializedJson = Encoding.UTF8.GetString(memory.Span);
        PropertiesEditor = new(asset, asset.Id, 0);
        PropertiesEditor.OnValueChanged += OnValueChanged;
    }

    public override void SaveAsset(EditorEngine engine)
    {
        if (_path is null)
        {
            throw new InvalidOperationException("Failed to save asset: Path is null");
        }
        if (_asset is null)
        {
            throw new InvalidOperationException("Failed to save asset: Asset is null");
        }
        using SafeMemoryHandle memory = engine.Assets.EncodeToBinary(_asset);
        File.WriteAllBytes(_path, memory.Span);
        _isModified = false;
        OnPropertyChanged(nameof(IsModified));
    }

    private void OnValueChanged()
    {
        _isModified = true;
        OnPropertyChanged(nameof(IsModified));
    }
}
