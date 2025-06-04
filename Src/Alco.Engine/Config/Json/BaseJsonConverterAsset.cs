

using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.IO;


namespace Alco.Engine;

public abstract class BaseJsonConverterAsset<T> : JsonConverter<T> where T : class
{
    protected readonly AssetSystem _assetSystem;

    public BaseJsonConverterAsset(AssetSystem assetSystem)
    {
        _assetSystem = assetSystem;
    }

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? path = reader.GetString();
        if (path == null)
        {
            return default;
        }
        return _assetSystem.Load<T>(path);
    }
}