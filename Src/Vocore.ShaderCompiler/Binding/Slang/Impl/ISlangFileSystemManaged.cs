
namespace SlangSharp;


public interface ISlangFileSystemManaged
{
    bool TryLoadFile(string path, out byte[] data);
}