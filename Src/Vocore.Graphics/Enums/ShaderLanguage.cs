namespace Vocore.Graphics
{
    public enum ShaderLanguage
    {
        Undefined,
        // supported
        HLSL,
        SPIRV,
        WGSL,
        // curently not supported
        GLSL,
        MSL,
        DXIL
    }
}