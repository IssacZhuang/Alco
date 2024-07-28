namespace Vocore.Graphics
{
    public enum ShaderLanguage
    {
        Undefined,
        // supported
        HLSL,
        SLANG,
        SPIRV,
        WGSL,
        // curently not supported
        GLSL,
        MSL,
        DXIL
    }
}