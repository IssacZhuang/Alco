
namespace SlangSharp;


public unsafe interface ISlangFileSystemManaged
{
    bool TryLoadFile(string path, out ISlangBlob* blob);
}