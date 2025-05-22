using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Alco.IO;

namespace Alco.Engine;

public class ConfigDatabase
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Configable>> _configs = new();

    // key extension, value config loader
    private readonly ConcurrentDictionary<string, IConfigLoader> _configLoaders = new();
    private readonly PriorityList<IFileSource> _fileSources = new PriorityList<IFileSource>((a, b) => a.Priority.CompareTo(b.Priority));

    private volatile bool _isDirty = false;

    public ConfigDatabase()
    {

    }

    public Configable GetConfig(string id, Type type)
    {
        TryUpdateConfigs();
        if (_configs.TryGetValue(type, out var typeConfigs))
        {
            if (typeConfigs.TryGetValue(id, out var config))
            {
                return config;
            }
        }

        throw new Exception($"Config with id {id} and type {type.Name} not found");
    }

    public bool TryGetConfig(string id, Type type, [MaybeNullWhen(false)] out Configable config)
    {
        TryUpdateConfigs();
        if (_configs.TryGetValue(type, out var typeConfigs))
        {
            return typeConfigs.TryGetValue(id, out config);
        }

        config = null;
        return false;
    }

    public void AddFileSource(IFileSource fileSource)
    {
        _fileSources.Add(fileSource);
        _isDirty = true;
    }

    public void RemoveFileSource(IFileSource fileSource)
    {
        _fileSources.Remove(fileSource);
        _isDirty = true;
    }

    public void RegisterConfigLoader(IConfigLoader configLoader)
    {
        foreach (var extension in configLoader.FileExtensions)
        {
            _configLoaders.TryAdd(extension, configLoader);
        }
        _isDirty = true;
    }

    public void UnregisterConfigLoader(IConfigLoader configLoader)
    {
        foreach (var extension in configLoader.FileExtensions)
        {
            if (_configLoaders.TryGetValue(extension, out var loader))
            {
                if (loader == configLoader)
                {
                    _configLoaders.TryRemove(extension, out _);
                }
            }
        }
        _isDirty = true;
    }


    private void TryUpdateConfigs()
    {
        if (!_isDirty)
        {
            return;
        }

        foreach (var fileSource in _fileSources)
        {
            foreach (var filename in fileSource.AllFileNames)
            {
                if (TryGetLoader(filename, out var loader) && fileSource.TryGetData(filename, out var data, out _))
                {
                    var config = loader.CreateConfig(filename, data.Span);
                    AddConfig(config);
                    data.Dispose();
                }
            }
        }
    }

    private bool TryGetLoader(string filename, [NotNullWhen(true)] out IConfigLoader? loader)
    {
        var extension = Path.GetExtension(filename);
        return _configLoaders.TryGetValue(extension, out loader);
    }

    private void AddConfig(Configable config)
    {
        ConcurrentDictionary<string, Configable> typeConfigs = _configs.GetOrAdd(config.GetType(), _ => new());
        typeConfigs.TryAdd(config.Id, config);
    }

}
