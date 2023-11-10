using System;
using System.Runtime.InteropServices;

namespace Vocore.ShaderConductor
{
    public static class RuntimeHelper
    {
        internal static Native_ShaderStage ShaderStageToNative(ShaderStage stage)
        {
            return (Native_ShaderStage)stage;
        }

        internal static ShaderStage ShaderStageFromNative(Native_ShaderStage stage)
        {
            return (ShaderStage)stage;
        }

        internal static Native_ShaderModel ShaderModelToNative(ShaderModel model)
        {
            return new Native_ShaderModel
            {
                major = model.major,
                minor = model.minor
            };
        }
        public static ShaderStage ShaderStageFromVeldrid(Veldrid.ShaderStages stage)
        {
            switch (stage)
            {
                case Veldrid.ShaderStages.Vertex:
                    return ShaderStage.VertexShader;
                case Veldrid.ShaderStages.Fragment:
                    return ShaderStage.PixelShader;
                case Veldrid.ShaderStages.Geometry:
                    return ShaderStage.GeometryShader;
                case Veldrid.ShaderStages.TessellationControl:
                    return ShaderStage.HullShader;
                case Veldrid.ShaderStages.TessellationEvaluation:
                    return ShaderStage.DomainShader;
                case Veldrid.ShaderStages.Compute:
                    return ShaderStage.ComputeShader;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

        public static ShadingLanguage GetShadingLanguage(Veldrid.GraphicsBackend backend)
        {
            switch (backend)
            {
                case Veldrid.GraphicsBackend.Direct3D11:
                    return ShadingLanguage.Hlsl;
                case Veldrid.GraphicsBackend.Vulkan:
                    return ShadingLanguage.Glsl;
                case Veldrid.GraphicsBackend.OpenGL:
                    return ShadingLanguage.Glsl;
                case Veldrid.GraphicsBackend.OpenGLES:
                    return ShadingLanguage.Essl;
                case Veldrid.GraphicsBackend.Metal:
                    return ShadingLanguage.Msl_macOS;
                default:
                    throw new ArgumentOutOfRangeException(nameof(backend), backend, null);
            }
        }
    }
}