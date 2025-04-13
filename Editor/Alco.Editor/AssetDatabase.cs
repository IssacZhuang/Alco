
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alco.IO;
using Alco.Rendering;
using AvaloniaEdit.Utils;

namespace Alco.Editor;

public class AssetDatabase
{
    private struct ConfigMeta
    {
        public string Path;
        public Type Type;

        public ConfigMeta(string path, Type type)
        {
            Path = path;
            Type = type;
        }
    }

    private readonly AssetSystem _assetSystem;
    private ConcurrentDictionary<string, ConfigMeta> _configMetas = new();

    private readonly TypeHelper _typeHelper;

    private volatile HashSet<string> _configFileExtensions = [];
    private volatile HashSet<string> _textureFileExtensions = [];

    private Task? _taskUpdateConfig;
    private CancellationTokenSource? _taskUpdateConfigToken;

    public AssetDatabase(AssetSystem assetSystem)
    {
        _assetSystem = assetSystem;
        _typeHelper = new TypeHelper(true);
    }

    public void Initialize()
    {
        UpdateConfigFileExtensions();
        UpdateTextureFileExtensions();
    }

    private (Task, CancellationTokenSource) UpdateConfigMetaAsync()
    {
        _taskUpdateConfigToken = new CancellationTokenSource();
        _taskUpdateConfig = Task.Run(UpdateConfigFileExtensions, _taskUpdateConfigToken.Token);
        return (_taskUpdateConfig, _taskUpdateConfigToken);
    }

    private void UpdateConfigMeta()
    {
        IEnumerable<string> allAssetPaths = _assetSystem.AllFileNames;
        foreach (var assetPath in allAssetPaths)
        {
            string extension = Path.GetExtension(assetPath);
            if (_configFileExtensions.Contains(extension)&&_assetSystem.TryLoadRaw(assetPath, out SafeMemoryHandle handle))
            {
                string json = Encoding.UTF8.GetString(handle.Span);
                JsonDocument doc = JsonDocument.Parse(json);
                //get $type
                string type = doc.RootElement.GetProperty("$type").GetString() ?? throw new Exception("type is null");
            }
        }
    }

    private void UpdateConfigFileExtensions()
    {
        HashSet<string> configFileExtensions = new();
        foreach (var loader in _assetSystem.AllAssetLoaders)
        {
            if (loader.CanHandleType(typeof(Configable)))
            {
                configFileExtensions.AddRange(loader.FileExtensions);
            }
        }
        _configFileExtensions = configFileExtensions;
    }

    private void UpdateTextureFileExtensions()
    {
        HashSet<string> textureFileExtensions = new();
        foreach (var loader in _assetSystem.AllAssetLoaders)
        {
            if (loader.CanHandleType(typeof(Texture2D)))
            {
                textureFileExtensions.AddRange(loader.FileExtensions);
            }
        }
        _textureFileExtensions = textureFileExtensions;
    }
}

