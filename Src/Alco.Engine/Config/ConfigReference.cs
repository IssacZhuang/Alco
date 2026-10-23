namespace Alco.Engine;

/// <summary>A reference to a config of type <typeparamref name="T"/> identified by its id.</summary>
/// <typeparam name="T">The config type.</typeparam>
public sealed class ConfigReference<T> : IEquatable<ConfigReference<T>> where T : Configable
{
    private readonly ConfigDatabase _database;
    private readonly Lazy<T> _config;
    /// <summary>The referenced config id.</summary>
    public string Id { get; }

    /// <summary>Gets the config, loading it from the database on first access.</summary>
    public T Config => _config.Value;

    /// <summary>Creates a reference to the config with the given id.</summary>
    /// <param name="database">The database that resolves the config.</param>
    /// <param name="id">The config id.</param>
    public ConfigReference(ConfigDatabase database, string id)
    {
        _database = database;
        Id = id;
        _config = new(LoadConfig, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private T LoadConfig()
    {
        return _database.GetConfig<T>(Id);
    }

    /// <summary>Resolves the referenced config.</summary>
    /// <param name="reference">The config reference.</param>
    /// <returns>The referenced config.</returns>
    public static implicit operator T(ConfigReference<T> reference)
    {
        return reference._config.Value;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ConfigReference<T>);
    }

    /// <summary>Checks whether two references have the same config id.</summary>
    /// <param name="other">The reference to compare with.</param>
    /// <returns>True when both references have the same id.</returns>
    public bool Equals(ConfigReference<T>? other)
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

    /// <summary>Checks whether two references have the same config id.</summary>
    /// <param name="left">The left reference.</param>
    /// <param name="right">The right reference.</param>
    /// <returns>True when both references have the same id.</returns>
    public static bool operator ==(ConfigReference<T>? left, ConfigReference<T>? right)
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

    /// <summary>Checks whether two references have different config ids.</summary>
    /// <param name="left">The left reference.</param>
    /// <param name="right">The right reference.</param>
    /// <returns>True when the references have different ids.</returns>
    public static bool operator !=(ConfigReference<T>? left, ConfigReference<T>? right)
    {
        return !(left == right);
    }
}
