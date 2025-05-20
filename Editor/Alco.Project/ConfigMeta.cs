
namespace Alco.Project;

public class ConfigMeta(string path, Type type)
{
    public string Path { get; } = path;
    public Type Type { get; } = type;
}
