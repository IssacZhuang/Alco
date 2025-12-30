namespace Alco.Engine;

/// <summary>
/// Represents an optional reference to a configuration object.
/// Returns null if the Id is empty.
/// </summary>
/// <typeparam name="T">The type of the configuration object.</typeparam>
public sealed class ConfigReferenceOptional<T> : IEquatable<ConfigReferenceOptional<T>> where T : Configable
{
    private readonly ConfigDatabase _database;
    private readonly Lazy<T?> _config;

    /// <summary>
    /// Gets the identifier of the configuration.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the configuration object, or null if the Id is empty.
    /// </summary>
    public T? Config => _config.Value;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigReferenceOptional{T}"/> class.
    /// </summary>
    /// <param name="database">The configuration database.</param>
    /// <param name="id">The identifier of the configuration.</param>
    public ConfigReferenceOptional(ConfigDatabase database, string id)
    {
        _database = database;
        Id = id;
        _config = new(LoadConfig, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private T? LoadConfig()
    {
        if (string.IsNullOrEmpty(Id))
        {
            return null;
        }
        return _database.GetConfig<T>(Id);
    }

    /// <summary>
    /// Implicitly converts the reference to the configuration object.
    /// </summary>
    /// <param name="reference">The configuration reference.</param>
    public static implicit operator T?(ConfigReferenceOptional<T> reference)
    {
        return reference._config.Value;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ConfigReferenceOptional<T>);
    }

    public bool Equals(ConfigReferenceOptional<T>? other)
    {
        if (other is null)
        {
            return false;
        }
        if (ReferenceEquals(this, other))
        {
            return true;
        }
        return Id == other.Id;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    public static bool operator ==(ConfigReferenceOptional<T>? left, ConfigReferenceOptional<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }
        if (left is null || right is null)
        {
            return false;
        }
        return left.Equals(right);
    }

    public static bool operator !=(ConfigReferenceOptional<T>? left, ConfigReferenceOptional<T>? right)
    {
        return !(left == right);
    }
}
