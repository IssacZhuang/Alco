using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.IO;

namespace Alco.Engine;

public class ConfigDatabase
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Configable>> _configs = new();

    private readonly JsonPreprocessor _jsonPreprocessor;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    private readonly Action<string> _onError;

    private volatile bool _isDirty = false;

    public ConfigDatabase(ReadOnlySpan<JsonConverter> converters, Action<string> onInfo, Action<string> onWarning, Action<string> onError)
    {
        ArgumentNullException.ThrowIfNull(onInfo);
        ArgumentNullException.ThrowIfNull(onWarning);
        ArgumentNullException.ThrowIfNull(onError);
        _onError = onError;

        _jsonSerializerOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new ConfigJsonTypeResolver(),
            WriteIndented = true,
        };
        foreach (var converter in converters)
        {
            _jsonSerializerOptions.Converters.Add(converter);
        }

        _jsonSerializerOptions.MakeReadOnly();

        _jsonPreprocessor = new JsonPreprocessor(onInfo, onWarning, onError);
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
        _jsonPreprocessor.AddFileSource(fileSource);
        _isDirty = true;
    }

    public void RemoveFileSource(IFileSource fileSource)
    {
        _jsonPreprocessor.RemoveFileSource(fileSource);
        _isDirty = true;
    }


    private void TryUpdateConfigs()
    {
        if (!_isDirty)
        {
            return;
        }

        _configs.Clear();
        _jsonPreprocessor.Preprocess();

        Parallel.ForEach(_jsonPreprocessor.AllDocuments, document =>
        {
            try
            {
                var config = JsonSerializer.Deserialize<Configable>(document, _jsonSerializerOptions);
                if (config != null)
                {
                    AddConfig(config);
                }
            }
            catch (Exception ex)
            {
                _onError($"Error deserializing config: {ex.Message}");
            }
        });
        _isDirty = false;
    }

    private void AddConfig(Configable config)
    {
        ConcurrentDictionary<string, Configable> typeConfigs = _configs.GetOrAdd(config.GetType(), static type => new());
        typeConfigs.TryAdd(config.Id, config);
    }

    internal string[] DebugListAllConfigs()
    {
        return _configs.SelectMany(type => type.Value.Select(config => $"{type.Key}: {config.Value.Id} ({config.Value.GetType().Name})")).ToArray();
    }
}
