using Alco.Engine;

namespace Alco.Project;

public partial class AlcoProject
{
    public IEnumerable<Type> ConfigTypes => _typeDatabase.ConfigTypes;

    public IEnumerable<ConfigMeta> ConfigMetas => _assetDatabase.ConfigMetas;

    public void WriteConfig(Configable config, string path, string filename)
    {
        using SafeMemoryHandle handle = _assetSystem.EncodeToBinary(config);
        File.WriteAllBytes(Path.Combine(path, filename+".json"), handle.AsReadOnlySpan());
    }
}
