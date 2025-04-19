
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

    public AssetDatabase(AssetSystem assetSystem)
    {
        _assetSystem = assetSystem;
        _typeHelper = new TypeHelper(true);
        UpdateConfigFileExtensions();
        UpdateTextureFileExtensions();
        UpdateConfigMeta(CancellationToken.None);
    }

    public void UpdateConfigMeta(CancellationToken token)
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
                    configMetas.TryAdd(assetPath, new ConfigMeta(assetPath, type));
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
                foreach (var extension in loader.FileExtensions)
                {
                    configFileExtensions.Add(extension);
                }
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
                foreach (var extension in loader.FileExtensions)
                {
                    textureFileExtensions.Add(extension);
                }
            }
        }
        _textureFileExtensions = textureFileExtensions;
    }
}

