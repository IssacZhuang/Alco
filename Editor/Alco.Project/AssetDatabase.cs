
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Project;

public class AssetDatabase
{
    private struct ConfigMeta(string path, Type type)
    {
        public string Path = path;
        public Type Type = type;
    }

    private readonly AssetSystem _assetSystem;
    private volatile Dictionary<string, ConfigMeta> _configMetas = [];

    private readonly TypeHelper _typeHelper;

    private volatile HashSet<string> _configFileExtensions = [];
    private volatile HashSet<string> _textureFileExtensions = [];

    private BackgroundTask? _taskUpdateConfig;

    public AssetDatabase(AssetSystem assetSystem)
    {
        _assetSystem = assetSystem;
        _typeHelper = new TypeHelper(true);
        UpdateConfigFileExtensions();
        UpdateTextureFileExtensions();
    }

    public bool TryGetConfigType(string path, [NotNullWhen(true)] out Type? type)
    {
        _taskUpdateConfig?.TryWait();

        if (_configMetas.TryGetValue(path, out ConfigMeta meta))
        {
            type = meta.Type;
            return true;
        }
        type = null;
        return false;
    }

    public void UpdateConfigMeta()
    {
        if (_taskUpdateConfig != null && !_taskUpdateConfig.IsCompleted)
        {
            _taskUpdateConfig.Cancel();
        }
        _taskUpdateConfig = UpdateConfigMetaAsync();
    }

    private BackgroundTask UpdateConfigMetaAsync()
    {
        return BackgroundTask.Run(UpdateConfigMeta);
    }

    private void UpdateConfigMeta(CancellationToken token)
    {
        IEnumerable<string> allAssetPaths = _assetSystem.AllFileNames;
        Dictionary<string, ConfigMeta> configMetas = new();
        foreach (var assetPath in allAssetPaths)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                string extension = Path.GetExtension(assetPath);
                if (_configFileExtensions.Contains(extension) && _assetSystem.TryLoadRaw(assetPath, out SafeMemoryHandle handle))
                {
                    string json = Encoding.UTF8.GetString(handle.Span);
                    using JsonDocument doc = JsonDocument.Parse(json);
                    //get $type
                    string strType = doc.RootElement.GetProperty("$type").GetString() ?? throw new Exception("type is null");
                    Type? type = _typeHelper.FindType(strType);
                    if (type == null)
                    {
                        Log.Error($"type {strType} not found");
                        continue;
                    }
                    _configMetas.TryAdd(assetPath, new ConfigMeta(assetPath, type));
                }
            }
            catch (Exception e)
            {
                Log.Error($"error loading config meta for {assetPath}: {e.Message}");
            }
        };

        _configMetas = configMetas;
    }

    private void UpdateConfigFileExtensions()
    {
        HashSet<string> configFileExtensions = new();
        foreach (var loader in _assetSystem.AllAssetLoaders)
        {
            if (loader.CanHandleType(typeof(Configable)))
            {
                configFileExtensions.UnionWith(loader.FileExtensions);
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
                textureFileExtensions.UnionWith(loader.FileExtensions);
            }
        }
        _textureFileExtensions = textureFileExtensions;
    }
}

