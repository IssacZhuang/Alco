namespace Alco.Graphics
{
    public enum ShaderLanguage
    {
        Undefined,
        // supported
        HLSL,
        SLANG,
        SPIRV,
        WGSL,
        DXIL,
        MSL,
        // currently not supported
        GLSL
    }
}
