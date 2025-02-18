namespace Alco.IO;

public interface IAssetEncoder
{
    IEnumerable<Type> GetSupportedTypes();
    SafeMemoryHandle Encode(object asset);
}

