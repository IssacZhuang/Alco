using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Alco.IO;

namespace Alco.Engine;

public class ConfigDatabase
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Configable>> _configs = new();

    // key extension, value config loader
    private readonly Dictionary<string, IConfigLoader> _configLoaders = new();
    private readonly Dictionary<string, IFileSource> _fileEntries = new Dictionary<string, IFileSource>();
    private readonly PriorityList<IFileSource> _fileSources = new PriorityList<IFileSource>((a, b) => a.Priority.CompareTo(b.Priority));

    public ConfigDatabase()
    {

    }

    public Configable GetConfig(string id, Type type)
    {
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
        if (_configs.TryGetValue(type, out var typeConfigs))
        {
            return typeConfigs.TryGetValue(id, out config);
        }

        config = null;
        return false;
    }

    public void AddConfig(Configable config)
    {
        ConcurrentDictionary<string, Configable> typeConfigs = _configs.GetOrAdd(config.GetType(), _ => new());
        typeConfigs.TryAdd(config.Id, config);
    }

    public void RemoveConfig(Configable config)
    {
        if (_configs.TryGetValue(config.GetType(), out var typeConfigs))
        {
            typeConfigs.TryRemove(config.Id, out _);
        }
    }

    public void Clear()
    {
        _configs.Clear();
    }
    

}
