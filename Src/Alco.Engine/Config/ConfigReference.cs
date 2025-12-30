namespace Alco.Engine;

public sealed class ConfigReference<T> : IEquatable<ConfigReference<T>> where T : Configable
{
    private readonly ConfigDatabase _database;
    private readonly Lazy<T> _config;
    public string Id { get; }

    public T Config => _config.Value;

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

    public static implicit operator T(ConfigReference<T> reference)
    {
        return reference._config.Value;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ConfigReference<T>);
    }

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

    public static bool operator !=(ConfigReference<T>? left, ConfigReference<T>? right)
    {
        return !(left == right);
    }
}
