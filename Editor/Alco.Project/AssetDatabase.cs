
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Alco.IO;
using Alco.Engine;
using Alco.Rendering;

namespace Alco.Project;

public class AssetDatabase
{
    private readonly AssetSystem _assetSystem;
    private readonly ConcurrentDictionary<string, ConfigMeta> _configMetas = [];
    private readonly Lock _configExtensionsLock = new();
    private readonly Lock _textureExtensionsLock = new();

    private readonly TypeHelper _typeHelper;

    private volatile HashSet<string> _configFileExtensions = [];
    private volatile HashSet<string> _textureFileExtensions = [];

    public IEnumerable<ConfigMeta> ConfigMetas => _configMetas.Values;

    public AssetDatabase(AssetSystem assetSystem)
    {
        _assetSystem = assetSystem;
        _typeHelper = new TypeHelper(true);
        UpdateConfigFileExtensions();
        UpdateTextureFileExtensions();
        UpdateConfigMetas();
    }

    public void MarkAsChanged(string path)
    {
        if (!IsConfigFile(path))
        {
            return;
        }

        //todo update meta in deffered mode
        if (!TryUpdateConfigMeta(path))
        {
            Log.Error($"Failed to get config type for {path}");
        }
    }

    public void MarkAsCreate(string path)
    {
        if (!IsConfigFile(path))
        {
            return;
        }
        // Similar to MarkAsChanged, try to update the config meta
        // We don't need to log an error if it fails, as it might not be a config file
        if (!TryUpdateConfigMeta(path))
        {
            Log.Error($"Failed to get config type for {path}");
        }
    }

    public void MarkAsDelete(string path)
    {
        RemoveAssetMeta(path);
    }

    private void UpdateConfigMetas()
    {
        IEnumerable<string> allAssetPaths = _assetSystem.AllFileNames;
        // foreach (var assetPath in allAssetPaths)
        Parallel.ForEach(allAssetPaths, assetPath =>
        {
            try
            {
                if (!IsConfigFile(assetPath))
                {
                    return;
                }

                if (TryGetConfigType(assetPath, out Type? type))
                {
                    _configMetas.AddOrUpdate(assetPath, new ConfigMeta(assetPath, type), (_, _) => new ConfigMeta(assetPath, type));
                }
                else
                {
                    Log.Error($"Failed to get config type for {assetPath}");
                }
            }
            catch (Exception e)
            {
                Log.Error($"error loading config meta for {assetPath}: {e}");
            }
        });
    }

    private bool TryUpdateConfigMeta(string path)
    {
        if (TryGetConfigType(path, out Type? type))
        {
            _configMetas.AddOrUpdate(path, new ConfigMeta(path, type), (_, _) => new ConfigMeta(path, type));
            return true;
        }

        return false;
    }

    private bool IsConfigFile(string assetPath)
    {
        string extension = Path.GetExtension(assetPath);
        return _configFileExtensions.Contains(extension);
    }

    private bool TryGetConfigType(string assetPath, [NotNullWhen(true)] out Type? type)
    {
        if (_assetSystem.TryLoadRaw(assetPath, out SafeMemoryHandle handle))
        {
            string json = Encoding.UTF8.GetString(handle.Span);
            using JsonDocument doc = JsonDocument.Parse(json);
            string strType = doc.RootElement.GetProperty("$type").GetString() ?? throw new Exception("type is null");
            type = _typeHelper.FindType(strType);
            return type != null;
        }
        type = null;
        return false;
    }

    private void UpdateConfigFileExtensions()
    {
        lock (_configExtensionsLock)
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
    }

    private void UpdateTextureFileExtensions()
    {
        lock (_textureExtensionsLock)
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

    private void RemoveAssetMeta(string assetPath)
    {
        _configMetas.TryRemove(assetPath, out _);
    }
}

