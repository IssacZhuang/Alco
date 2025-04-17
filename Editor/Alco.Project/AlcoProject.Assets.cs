using Alco.IO;

namespace Alco.Project;

public partial class AlcoProject
{
    public void WriteConfig(Configable config, string path, string filename)
    {
        using SafeMemoryHandle handle = _engine.Assets.EncodeToBinary(config);
        File.WriteAllBytes(Path.Combine(path, filename+".json"), handle.Span);
    }
}
