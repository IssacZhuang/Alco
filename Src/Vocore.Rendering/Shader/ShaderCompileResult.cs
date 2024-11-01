using Vocore.Graphics;

namespace Vocore.Rendering;

public class ShaderCompileResult
{
    public ShaderVariant[] Variants { get; }
    public ShaderCompileResult(ShaderVariant[] variants)
    {
        Variants = variants;
    }
}