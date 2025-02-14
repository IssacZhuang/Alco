
namespace Alco.IO;

/// <summary>
/// The base class of asset loader
/// </summary>
public abstract class BaseAssetLoader : IAssetLoader
{
    public abstract string Name { get; }

    public abstract IReadOnlyList<string> FileExtensions { get; }

    public abstract bool CanHandleType(Type type);

    public abstract object CreateAsset(string filename, ReadOnlySpan<byte> data, Type targetType);

    public virtual void OnAssetLoaded(object asset)
    {
        //do nothing by default
    }
}

/// <summary>
/// The single type asset loader
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class BaseAssetLoader<T> : IAssetLoader
{

    public abstract string Name { get; }

    public abstract IReadOnlyList<string> FileExtensions { get; }

    public virtual bool CanHandleType(Type type)
    {
        return type == typeof(T);
    }

    public abstract object CreateAsset(string filename, ReadOnlySpan<byte> data, Type targetType);

    public virtual void OnAssetLoaded(object asset)
    {
        //do nothing by default
    }
}

