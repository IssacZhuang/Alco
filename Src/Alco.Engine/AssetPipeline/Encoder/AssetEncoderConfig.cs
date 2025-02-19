using System.Text;
using System.Text.Json;
using Alco.IO;

namespace Alco.Engine;

public class AssetEncoderConfig : IAssetEncoder
{
    public SafeMemoryHandle Encode(object asset)
    {
        string json = JsonSerializer.Serialize(asset);
        return new SafeMemoryHandle(Encoding.UTF8.GetBytes(json));
    }

    public IEnumerable<Type> GetSupportedTypes()
    {
        //all class that inherit from BaseConfig and BaseConfig itself
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsSubclassOf(typeof(BaseConfig)))
            .Concat([typeof(BaseConfig)]);
    }
}
