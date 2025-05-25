using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Thread-safe configuration database that manages config objects from multiple file sources
/// </summary>
public class ConfigDatabase
{
    private readonly Dictionary<Type, Dictionary<string, Configable>> _configs = new();
    private readonly ArrayBuffer<Configable?> _tempConfigs = new();

    private readonly JsonPreprocessor _jsonPreprocessor;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    private readonly Action<string> _onError;

    private volatile bool _isDirty = false;
    private readonly Lock _updateLock = new();

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

    /// <summary>
    /// Thread-safely updates the configuration cache if needed
    /// </summary>
    public void TryUpdateConfigs()
    {
        // First check without lock (performance optimization)
        if (!_isDirty)
        {
            return;
        }

        // Enter critical section with double-checked locking
        lock (_updateLock)
        {
            // Second check inside lock to ensure only one thread updates
            if (!_isDirty)
            {
                return;
            }

            _configs.Clear();
            _jsonPreprocessor.Preprocess();

            _tempConfigs.EnsureSizeWithoutCopy(_jsonPreprocessor.AllDocuments.Count);
            _tempConfigs.Clear();

            IReadOnlyList<JsonDocument> documents = _jsonPreprocessor.AllDocuments;

            Parallel.For(0, documents.Count, i =>
            {
                try
                {
                    var config = JsonSerializer.Deserialize<Configable>(documents[i], _jsonSerializerOptions);
                    if (config != null)
                    {
                        _tempConfigs[i] = config;
                    }
                }
                catch (Exception ex)
                {
                    _onError($"Error deserializing config: {ex}");
                }
            });

            for (int i = 0; i < _tempConfigs.Length; i++)
            {
                var config = _tempConfigs[i];
                if (config != null)
                {
                    AddConfig(config);
                }
            }
            _isDirty = false;
        }
    }

    private void AddConfig(Configable config)
    {
        if (!_configs.TryGetValue(config.GetType(), out var typeConfigs))
        {
            typeConfigs = new();
            _configs[config.GetType()] = typeConfigs;
        }
        typeConfigs.TryAdd(config.Id, config);
    }

    /// <summary>
    /// Returns debug information about all loaded configurations
    /// </summary>
    /// <returns>Array of strings describing all loaded configurations</returns>
    internal string[] DebugListAllConfigs()
    {
        return _configs.SelectMany(type => type.Value.Select(config => $"{type.Key}: {config.Value.Id} ({config.Value.GetType().Name})")).ToArray();
    }
}


