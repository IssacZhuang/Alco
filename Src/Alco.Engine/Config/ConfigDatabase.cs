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
    /// [Thread-safe] Get all configs of a specific type.
    /// </summary>
    /// <param name="type">The type of the configs to get.</param>
    /// <returns>All configs of the specified type.</returns>
    public IEnumerable<Configable> GetConfigs(Type type)
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
    /// Initializes a new instance of the <see cref="ConfigDatabase"/> class.
    /// </summary>
    /// <param name="polymorphicTypes">Types to support for polymorphic JSON serialization</param>
    /// <param name="converters">Custom JSON converters to use for deserialization</param>
    /// <param name="onInfo">Callback for informational messages</param>
    /// <param name="onWarning">Callback for warning messages</param>
    /// <param name="onError">Callback for error messages</param>
    /// <exception cref="ArgumentNullException">Thrown when any callback parameter is null</exception>
    public ConfigDatabase(ReadOnlySpan<Type> polymorphicTypes, ReadOnlySpan<JsonConverter> converters, Action<string> onError)
    {
        ArgumentNullException.ThrowIfNull(onError);
        _onError = onError;

        List<Type> polymorphicTypeList = new();
        for (int i = 0; i < polymorphicTypes.Length; i++)
        {
            polymorphicTypeList.Add(polymorphicTypes[i]);
        }
        polymorphicTypeList.Add(typeof(Configable));

        _jsonSerializerOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new PolymorphicJsonTypeResolver(polymorphicTypeList.ToArray()),
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        foreach (var converter in converters)
        {
            _jsonSerializerOptions.Converters.Add(converter);
        }

        _jsonSerializerOptions.MakeReadOnly();

        _jsonPreprocessor = new JsonPreprocessor(onError);
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
    /// Try to update configs from all file sources if it is dirty.
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

            // resolve references
            Parallel.ForEach(_configsList, config =>
            {
                ResolveReferences(config);
            });

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

    private void ResolveReferences(object? @object, string path = "", int depth = 0)
    {
        // Check maximum recursion depth to prevent infinite recursion
        if (depth >= 64)
        {
            _onError($"Maximum recursion depth (64) exceeded while resolving references at path: {path}");
            return;
        }

        if (@object == null)
        {
            return;
        }

        var type = @object.GetType();
        if (type.IsPrimitive || type.IsEnum || type.IsValueType || !type.IsClass)
        {
            return;
        }

        if (@object is string)
        {
            //string never has references
            return;
        }

        if (UtilsType.IsGenericList(type, out var genericType))
        {
            ResolveListReferences(@object, path, genericType, depth + 1);
        }
        else if (UtilsType.IsGenericDictionary(type, out var keyType, out var valueType))
        {
            ResolveDictionaryReferences(@object, path, keyType, valueType, depth + 1);
        }

        AccessTypeInfo accessTypeInfo = GetAccessTypeInfo(@object.GetType());
        foreach (var accessMember in accessTypeInfo.Members)
        {
            var value = accessMember.GetValue<object>(@object);
            if (value is Configable)
            {
                ResolveConfigProperty(@object, accessMember.Name, accessMember);
            }
            else
            {
                ResolveReferences(value, path + "." + accessMember.Name, depth + 1);
            }
        }
    }

    private void ResolveConfigProperty(object @object, string propertyName, AccessMemberInfo accessMember)
    {
        try
        {
            var config = accessMember.GetValue<Configable>(@object);

            if (config == null)
            {
                _onError($"Config reference for property {propertyName} is null");
                return;
            }

            Type type = config.GetType();

            if (InternalTryGetConfig(config.Id, type, out var resolvedConfig))
            {
                accessMember.SetValue(@object, resolvedConfig);
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

    /// <summary>
    /// Resolves configuration references within list objects.
    /// </summary>
    /// <param name="list">The list object to resolve references in</param>
    /// <param name="propertyName">The property name for error reporting</param>
    /// <param name="genericType">The generic type of the list elements</param>
    /// <param name="depth">Current recursion depth</param>
    private void ResolveListReferences(object list, string propertyName, Type genericType, int depth)
    {
        try
        {
            if (genericType.IsAssignableTo(typeof(Configable)))
            {
                // If the list contains Configable objects, resolve each element
                if (list is IList<Configable> configList)
                {
                    for (int i = 0; i < configList.Count; i++)
                    {
                        var config = configList[i];
                        if (config != null && InternalTryGetConfig(config.Id, config.GetType(), out var resolvedConfig))
                        {
                            configList[i] = resolvedConfig;
                        }
                        else if (config != null)
                        {
                            _onError($"Config reference(id: {config.Id}) in list property {propertyName}[{i}] is not found");
                        }
                    }
                }
                else
                {
                    // Handle non-generic IList case using reflection
                    if (list is System.Collections.IList nonGenericList)
                    {
                        for (int i = 0; i < nonGenericList.Count; i++)
                        {
                            if (nonGenericList[i] is Configable config)
                            {
                                if (InternalTryGetConfig(config.Id, config.GetType(), out var resolvedConfig))
                                {
                                    nonGenericList[i] = resolvedConfig;
                                }
                                else
                                {
                                    _onError($"Config reference(id: {config.Id}) in list property {propertyName}[{i}] is not found");
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // If the list contains non-Configable objects, recursively resolve references in each element
                if (list is System.Collections.IList nonGenericList)
                {
                    for (int i = 0; i < nonGenericList.Count; i++)
                    {
                        ResolveReferences(nonGenericList[i], $"{propertyName}[{i}]", depth);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _onError($"Error resolving list references for property {propertyName} : {ex}");
        }
    }

    private void ResolveDictionaryReferences(object dictionary, string propertyName, Type keyType, Type valueType, int depth)
    {
        try
        {
            if (valueType.IsAssignableTo(typeof(Configable)))
            {
                // If the dictionary values are Configable objects, resolve each value
                if (dictionary is IDictionary<object, Configable> configDict)
                {
                    var keysToUpdate = new List<object>();
                    foreach (var kvp in configDict)
                    {
                        if (kvp.Value != null && InternalTryGetConfig(kvp.Value.Id, kvp.Value.GetType(), out var resolvedConfig))
                        {
                            keysToUpdate.Add(kvp.Key);
                        }
                        else if (kvp.Value != null)
                        {
                            _onError($"Config reference(id: {kvp.Value.Id}) in dictionary property {propertyName}[{kvp.Key}] is not found");
                        }
                    }

                    foreach (var key in keysToUpdate)
                    {
                        var originalConfig = configDict[key];
                        if (originalConfig != null && InternalTryGetConfig(originalConfig.Id, originalConfig.GetType(), out var resolvedConfig))
                        {
                            configDict[key] = resolvedConfig;
                        }
                    }
                }
                else
                {
                    // Handle non-generic IDictionary case using reflection
                    if (dictionary is System.Collections.IDictionary nonGenericDict)
                    {
                        var keysToUpdate = new List<object>();
                        foreach (System.Collections.DictionaryEntry entry in nonGenericDict)
                        {
                            if (entry.Value is Configable config)
                            {
                                if (InternalTryGetConfig(config.Id, config.GetType(), out var resolvedConfig))
                                {
                                    keysToUpdate.Add(entry.Key);
                                }
                                else
                                {
                                    _onError($"Config reference(id: {config.Id}) in dictionary property {propertyName}[{entry.Key}] is not found");
                                }
                            }
                        }

                        foreach (var key in keysToUpdate)
                        {
                            if (nonGenericDict[key] is Configable originalConfig &&
                                InternalTryGetConfig(originalConfig.Id, originalConfig.GetType(), out var resolvedConfig))
                            {
                                nonGenericDict[key] = resolvedConfig;
                            }
                        }
                    }
                }
            }
            else
            {
                // If the dictionary values are non-Configable objects, recursively resolve references in each value
                if (dictionary is System.Collections.IDictionary nonGenericDict)
                {
                    foreach (System.Collections.DictionaryEntry entry in nonGenericDict)
                    {
                        ResolveReferences(entry.Value, $"{propertyName}[{entry.Key}]", depth);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _onError($"Error resolving dictionary references for property {propertyName} : {ex}");
        }
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
}


