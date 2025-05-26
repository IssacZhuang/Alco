using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Configuration database that manages config objects from multiple file sources.
/// Provides fast lookup and automatic reloading of configurations when file sources are updated.
/// </summary>
public class ConfigDatabase
{
    private static readonly MemberAccessor s_memberAccessor = MemberAccessor.CreateCompatibleCachedAccessor();
    private readonly ConcurrentLruCache<Type, AccessTypeInfo> _accessTypeInfos = new(64);

    private readonly ConcurrentDictionary<Type, FrozenDictionary<string, Configable>> _configs = new();
    private readonly List<Configable> _configsList = new();
    private readonly ArrayBuffer<Configable?> _tempConfigs = new();

    private readonly JsonPreprocessor _jsonPreprocessor;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    private readonly Action<string> _onError;

    private volatile bool _isDirty = false;
    private readonly Lock _updateLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigDatabase"/> class.
    /// </summary>
    /// <param name="converters">Custom JSON converters to use for deserialization</param>
    /// <param name="onInfo">Callback for informational messages</param>
    /// <param name="onWarning">Callback for warning messages</param>
    /// <param name="onError">Callback for error messages</param>
    /// <exception cref="ArgumentNullException">Thrown when any callback parameter is null</exception>
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

    /// <summary>
    /// [Thread-safe] Gets a configuration object by ID and type.
    /// </summary>
    /// <param name="id">The unique identifier of the configuration</param>
    /// <param name="type">The type of the configuration to retrieve</param>
    /// <returns>The configuration object matching the specified ID and type</returns>
    /// <exception cref="Exception">Thrown when no configuration with the specified ID and type is found</exception>
    public Configable GetConfig(string id, Type type)
    {
        if (TryGetConfig(id, type, out var config))
        {
            return config;
        }

        throw new Exception($"Config with id {id} and type {type.Name} not found");
    }

    /// <summary>
    /// [Thread-safe] Gets a strongly-typed configuration object by ID.
    /// </summary>
    /// <typeparam name="T">The type of configuration to retrieve</typeparam>
    /// <param name="id">The unique identifier of the configuration</param>
    /// <returns>The configuration object of type T matching the specified ID</returns>
    /// <exception cref="Exception">Thrown when no configuration with the specified ID and type is found, or when the found configuration is not of the expected type</exception>
    public T GetConfig<T>(string id) where T : Configable
    {
        if (TryGetConfig(id, typeof(T), out var config))
        {
            if (config is T t)
            {
                return t;
            }
            else
            {
                throw new Exception($"Config with id {id} and type {typeof(T).Name} is not of type {typeof(T).Name}");
            }
        }

        throw new Exception($"Config with id {id} and type {typeof(T).Name} not found");
    }

    /// <summary>
    /// [Thread-safe] Attempts to get a configuration object by ID and type.
    /// </summary>
    /// <param name="id">The unique identifier of the configuration</param>
    /// <param name="type">The type of the configuration to retrieve</param>
    /// <param name="config">When this method returns, contains the configuration object if found; otherwise, null</param>
    /// <returns>true if a configuration with the specified ID and type was found; otherwise, false</returns>
    public bool TryGetConfig(string id, Type type, [MaybeNullWhen(false)] out Configable config)
    {
        TryUpdateConfigs();
        FrozenDictionary<string, Configable> typeConfigs = GetTypedConfigsDictionary(type);
        return typeConfigs.TryGetValue(id, out config);
    }

    /// <summary>
    /// [Thread-safe] Attempts to get a strongly-typed configuration object by ID.
    /// </summary>
    /// <typeparam name="T">The type of configuration to retrieve</typeparam>
    /// <param name="id">The unique identifier of the configuration</param>
    /// <param name="config">When this method returns, contains the configuration object if found and of the correct type; otherwise, null</param>
    /// <returns>true if a configuration with the specified ID and type was found and is of the correct type; otherwise, false</returns>
    public bool TryGetConfig<T>(string id, [MaybeNullWhen(false)] out T config) where T : Configable
    {
        if (TryGetConfig(id, typeof(T), out var tmpConfig))
        {
            if (tmpConfig is T t)
            {
                config = t;
                return true;
            }
            else
            {
                config = null;
                return false;
            }
        }
        config = null;
        return false;
    }

    /// <summary>
    /// [Not thread-safe] Adds a file source to the configuration database.
    /// The database will automatically reload configurations when files from this source are updated.
    /// </summary>
    /// <param name="fileSource">The file source to add</param>
    public void AddFileSource(IFileSource fileSource)
    {
        _jsonPreprocessor.AddFileSource(fileSource);
        _isDirty = true;
    }

    /// <summary>
    /// [Not thread-safe] Removes a file source from the configuration database.
    /// Configurations from this source will no longer be available after the next update.
    /// </summary>
    /// <param name="fileSource">The file source to remove</param>
    public void RemoveFileSource(IFileSource fileSource)
    {
        _jsonPreprocessor.RemoveFileSource(fileSource);
        _isDirty = true;
    }

    private void TryUpdateConfigs()
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

            _configsList.Clear();
            for (int i = 0; i < _tempConfigs.Length; i++)
            {
                var config = _tempConfigs[i];
                if (config != null)
                {
                    _configsList.Add(config);
                }
            }

            for (int i = 0; i < _configsList.Count; i++)
            {
                ResolveReferences(_configsList[i]);
            }
            _isDirty = false;
        }
    }

    private FrozenDictionary<string, Configable> GetTypedConfigsDictionary(Type type)
    {
        return _configs.GetOrAdd(
            type,
            BuildTypedConfigsDictionary
        );
    }

    private FrozenDictionary<string, Configable> BuildTypedConfigsDictionary(Type type)
    {
        Dictionary<string, Configable> table = new();
        for (int i = 0; i < _configsList.Count; i++)
        {
            var config = _configsList[i];
            if (config.GetType().IsAssignableTo(type))
            {
                //duplicate check
                if (!table.TryAdd(config.Id, config))
                {
                    _onError($"Duplicate config id {config.Id} for type {type.Name}");
                }
            }
        }
        return table.ToFrozenDictionary();
    }

    private void ResolveReferences(Configable config)
    {
        AccessTypeInfo accessTypeInfo = GetAccessTypeInfo(config.GetType());
        foreach (var accessMember in accessTypeInfo.Members)
        {
            if (accessMember.MemberType.IsAssignableTo(typeof(Configable)))
            {
                ResolveConfigProperty(config, accessMember.Name, accessMember);
            }
        }
    }

    private void ResolveConfigProperty(Configable asset, string propertyName, AccessMemberInfo accessMember)
    {
        try
        {
            var config = accessMember.GetValue<Configable>(asset);

            if (config == null)
            {
                _onError($"Config reference for property {propertyName} is null");
                return;
            }

            Type type = config.GetType();
            FrozenDictionary<string, Configable> typeConfigs = GetTypedConfigsDictionary(type);

            if (typeConfigs.TryGetValue(config.Id, out var resolvedConfig))
            {
                accessMember.SetValue(asset, resolvedConfig);
            }
            else
            {
                _onError($"Config reference(id: {config.Id}) for property {propertyName} is not found");
            }
        }
        catch (Exception ex)
        {
            _onError($"Error resolving config reference for property {propertyName} : {ex}");
        }
    }

    private AccessTypeInfo GetAccessTypeInfo(Type type)
    {
        return _accessTypeInfos.GetOrAdd(type, static (t) => new AccessTypeInfo(t, s_memberAccessor));
    }


    internal string[] DebugListAllConfigs()
    {
        return _configs.SelectMany(type => type.Value.Select(config => $"{type.Key}: {config.Value.Id} ({config.Value.GetType().Name})")).ToArray();
    }
}


