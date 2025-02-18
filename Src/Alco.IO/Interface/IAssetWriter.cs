namespace Alco.IO;

public interface IAssetWriter
{
    IEnumerable<Type> GetSupportedTypes();
    SafeMemoryHandle Write(object asset, out string path);
}

