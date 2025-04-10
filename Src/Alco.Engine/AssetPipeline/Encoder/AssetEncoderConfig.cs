using System.Text;
using System.Text.Json;
using Alco.IO;

namespace Alco.Engine;

public class AssetEncoderConfig : IAssetEncoder
{
    private readonly JsonSerializerOptions _options;

    public AssetEncoderConfig(JsonSerializerOptions options)
    {
        _options = options;
    }

    public SafeMemoryHandle Encode(object asset)
    {
        string json = JsonSerializer.Serialize(asset, _options);
        return new SafeMemoryHandle(Encoding.UTF8.GetBytes(json));
    }

    public IEnumerable<Type> GetSupportedTypes()
    {
        //all class that inherit from BaseConfig and BaseConfig itself
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsSubclassOf(typeof(Configable)))
            .Concat([typeof(Configable)]);
    }
}
