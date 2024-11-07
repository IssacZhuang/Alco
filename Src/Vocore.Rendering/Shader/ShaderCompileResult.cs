using Vocore.Graphics;

namespace Vocore.Rendering;

public class ShaderCompileResult
{
    public ShaderModulesInfo[] Variants { get; }
    public ShaderCompileResult(ShaderModulesInfo[] variants)
    {
        Variants = variants;
    }
}