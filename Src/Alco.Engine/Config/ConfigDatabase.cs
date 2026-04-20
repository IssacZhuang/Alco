using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco;
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

    private uint _version = 0;


    /// <summary>
    /// Gets the JSON serializer options used by this database.
    /// Public access for editor tools that need to serialize/deserialize configs.
    /// </summary>
    public JsonSerializerOptions SerializerOptions => _jsonSerializerOptions;

    /// <summary>
    /// [Thread-safe] All configs in the database.
    /// </summary>
    /// <value>All configs in the database.</value>
    public IReadOnlyList<Configable> Configs
    {
        get
        {
            TryUpdateConfigs();
            return _configsList;
        }
    }

    /// <summary>
    /// [Thread-safe] Gets the version number of the configuration database.
    /// The version is incremented whenever the configuration database is updated.
    /// </summary>
    /// <value>The current version number of the configuration database.</value>
    public uint Version => _version;

    /// <summary>
    /// [Thread-safe] Get all configs of a specific type.
    /// </summary>
    /// <param name="type">The type of the configs to get.</param>
    /// <returns>All configs of the specified type.</returns>
    public ImmutableArray<Configable> GetConfigs(Type type)
    {
        TryUpdateConfigs();
        return GetTypedConfigsDictionary(type).Values;
    }

    /// <summary>
    /// [Thread-safe] Get all configs of a specific type.
    /// </summary>
    /// <typeparam name="T">The type of the configs to get.</typeparam>
    /// <returns>All configs of the specified type.</returns>
    public IEnumerable<T> GetConfigs<T>() where T : Configable
    {
        TryUpdateConfigs();
        var values = GetTypedConfigsDictionary(typeof(T)).Values;
        foreach (var value in values)
        {
            if (value is T t)
            {
                yield return t;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigDatabase"/> class using explicit polymorphic root types
    /// combined with auto-discovered root types marked by <see cref="PolymorphicTypeAttribute"/>.
    /// </summary>
    /// <param name="polymorphicTypes">Additional root types to support for polymorphic JSON serialization</param>
    /// <param name="converters">Custom JSON converters to use for deserialization</param>
    /// <param name="onError">Callback for error messages</param>
    /// <exception cref="ArgumentNullException">Thrown when any callback parameter is null</exception>
    public ConfigDatabase(ReadOnlySpan<Type> polymorphicTypes, ReadOnlySpan<JsonConverter> converters, Action<string> onError)
    {
        ArgumentNullException.ThrowIfNull(onError);
        _onError = onError;

        HashSet<Type> rootTypes = new HashSet<Type>();

        // Auto-discovered roots by attribute
        var discovered = TypeUtility.FindTypesWithAttribute<PolymorphicTypeAttribute>();
        for (int i = 0; i < discovered.Length; i++)
        {
            rootTypes.Add(discovered[i]);
        }

        // Additional roots provided by caller
        for (int i = 0; i < polymorphicTypes.Length; i++)
        {
            rootTypes.Add(polymorphicTypes[i]);
        }

        // Always include Configable as polymorphic root
        rootTypes.Add(typeof(Configable));

        _jsonSerializerOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new PolymorphicJsonTypeResolver(rootTypes.ToArray()),
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        foreach (var converter in converters)
        {
            _jsonSerializerOptions.Converters.Add(converter);
        }

        _jsonSerializerOptions.Converters.Add(new JsonConverterConfigReferenceFactory(this));

        _jsonSerializerOptions.MakeReadOnly();

        _jsonPreprocessor = new JsonPreprocessor(onError);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigDatabase"/> class using auto-discovered polymorphic root types.
    /// All classes marked with <see cref="PolymorphicTypeAttribute"/> across loaded assemblies will be considered roots.
    /// </summary>
    /// <param name="converters">Custom JSON converters to use for deserialization</param>
    /// <param name="onError">Callback for error messages</param>
    public ConfigDatabase(ReadOnlySpan<JsonConverter> converters, Action<string> onError)
        : this(Array.Empty<Type>(), converters, onError)
    {
    }

    /// <summary>
    /// Occurs right before processing JSON items in the underlying preprocessor.
    /// Handlers can inspect and modify the in-memory JSON documents before they are merged.
    /// This event is forwarded to <see cref="JsonPreprocessor.BeforeProcessJsonDocument"/>.
    /// </summary>
    public event Action<IJsonPreprocessContext> BeforeProcessJsonDocument
    {
        add { _jsonPreprocessor.BeforeProcessJsonDocument += value; }
        remove { _jsonPreprocessor.BeforeProcessJsonDocument -= value; }
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
    public bool TryGetConfig(string id, Type type, [NotNullWhen(true)] out Configable? config)
    {
        TryUpdateConfigs();
        return InternalTryGetConfig(id, type, out config);
    }

    /// <summary>
    /// [Thread-safe] Attempts to get a strongly-typed configuration object by ID.
    /// </summary>
    /// <typeparam name="T">The type of configuration to retrieve</typeparam>
    /// <param name="id">The unique identifier of the configuration</param>
    /// <param name="config">When this method returns, contains the configuration object if found and of the correct type; otherwise, null</param>
    /// <returns>true if a configuration with the specified ID and type was found and is of the correct type; otherwise, false</returns>
    public bool TryGetConfig<T>(string id, [NotNullWhen(true)] out T? config) where T : Configable
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

    /// <summary>
    /// [Thread-safe] Marks the configuration database as dirty.
    /// Forces a reload of configurations on the next access/update.
    /// </summary>
    public void SetDirty()
    {
        _isDirty = true;
    }

    /// <summary>
    /// Try to update configs from all file sources if it is dirty.
    /// </summary>
    /// <param name="isForced">If true, forces an update even if the database is not dirty.</param>
    public void TryUpdateConfigs(bool isForced = false)
    {
        // First check without lock (performance optimization)
        if (!_isDirty && !isForced)
        {
            return;
        }

        // Enter critical section with double-checked locking
        lock (_updateLock)
        {
            // Second check inside lock to ensure only one thread updates
            if (!_isDirty && !isForced)
            {
                return;
            }

            _configs.Clear();
            _jsonPreprocessor.Preprocess();

            var documents = _jsonPreprocessor.AllDocuments.ToArray();

            _tempConfigs.SetSizeWithoutCopy(documents.Length);
            _tempConfigs.Clear();

            //serialize
            Parallel.For(0, documents.Length, i =>
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
                    string id = "unknown";
                    JsonDocument document = documents[i];
                    if (document != null && document.RootElement.TryGetProperty("Id", out var idProperty))
                    {
                        id = idProperty.GetString() ?? "unknown";
                    }
                    _onError($"Error deserializing config '{id}': {ex}");
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

            IncreaseVersion();
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



    private bool InternalTryGetConfig(string id, Type type, [NotNullWhen(true)] out Configable? config)
    {
        return GetTypedConfigsDictionary(type).TryGetValue(id, out config);
    }

    private AccessTypeInfo GetAccessTypeInfo(Type type)
    {
        return _accessTypeInfos.GetOrAdd(type, static (t) => new AccessTypeInfo(t, s_memberAccessor));
    }


    internal string[] DebugListAllConfigs()
    {
        return _configs.SelectMany(type => type.Value.Select(config => $"{type.Key}: {config.Value.Id} ({config.Value.GetType().Name})")).ToArray();
    }

    /// <summary>
    /// [Thread-safe] Increments the version number of the configuration database.
    /// This method is called whenever the configuration database is modified.
    /// </summary>
    public void IncreaseVersion()
    {
        unchecked
        {
            _version++;
        }
    }
}


