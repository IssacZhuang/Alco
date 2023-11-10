using System;

namespace Vocore.ShaderConductor
{
    public enum ShaderStage : int
    {
        VertexShader,
        PixelShader,
        GeometryShader,
        HullShader,
        DomainShader,
        ComputeShader,

        NumShaderStages,
    };
}