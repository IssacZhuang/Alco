using System;

namespace Vocore.ShaderConductor
{
    public enum ShadingLanguage : int
    {
        Dxil = 0,
        SpirV,

        Hlsl,
        Glsl,
        Essl,
        Msl_macOS,
        Msl_iOS,

        NumShadingLanguages,
    };

}