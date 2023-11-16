using System;
using System.Runtime.InteropServices;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.ShaderCross
{
    public static class HlslCross
    {
        public static readonly byte[] SpirvHeader = new byte[] { 0x03, 0x02, 0x23, 0x07 };

        public static VertexFragmentCompilationResult ComplieShader(string hlslCode, GraphicsBackend backend, string entryVertex = "VS", string entryFragment = "FS"){
            CrossCompileTarget target = GetCrossCompileTarget(backend);
            return ComplieShader(hlslCode, target, entryVertex, entryFragment);
        }

        public static VertexFragmentCompilationResult ComplieShader(string hlslCode, CrossCompileTarget target, string entryVertex = "VS", string entryFragment = "FS")
        {
            byte[] vsBytes = ComplieHlsl(hlslCode, entryVertex, ShaderConductor.ShaderStage.VertexShader, ShaderConductor.ShadingLanguage.SpirV);
            byte[] fsBytes = ComplieHlsl(hlslCode, entryFragment, ShaderConductor.ShaderStage.PixelShader, ShaderConductor.ShadingLanguage.SpirV);
            
            return SpirvCompilation.CompileVertexFragment(vsBytes, fsBytes, target, new CrossCompileOptions());
        }

        public static byte[] ComplieHlsl(string hlslCode, string entryPoint, ShaderConductor.ShaderStage stage, ShaderConductor.ShadingLanguage language)
        {
            ShaderConductor.SourceDesc sourceDesc = new ShaderConductor.SourceDesc
            {
                source = hlslCode,
                entryPoint = entryPoint,
                stage = stage,
            };

            ShaderConductor.OptionsDesc optionsDesc = ShaderConductor.OptionsDesc.Default;

            ShaderConductor.TargetDesc targetDesc = new ShaderConductor.TargetDesc
            {
                language = language,
                version = null,
            };

            ShaderConductor.Compile(ref sourceDesc, ref optionsDesc, ref targetDesc, out ShaderConductor.ResultDesc resultDesc);
            if (resultDesc.hasError)
            {
                throw new Exception(Marshal.PtrToStringAnsi(ShaderConductor.GetShaderConductorBlobData(resultDesc.errorWarningMsg)));
            }

            byte[] spirvBytes = ShaderBlobToByte(resultDesc.target);

            return spirvBytes;
        }

        public static CrossCompileTarget GetCrossCompileTarget(GraphicsBackend backend)
        {
            switch (backend)
            {
                case GraphicsBackend.Direct3D11:
                    return CrossCompileTarget.HLSL;
                //Vulkan can use Spirv directly
                // case GraphicsBackend.Vulkan:
                //     return CrossCompileTarget.GLSL;
                case GraphicsBackend.OpenGL:
                    return CrossCompileTarget.GLSL;
                case GraphicsBackend.OpenGLES:
                    return CrossCompileTarget.ESSL;
                case GraphicsBackend.Metal:
                    return CrossCompileTarget.MSL;
                default:
                    throw new Exception("Unsupported backend: " + backend);
            }
        }

        private static byte[] ShaderBlobToByte(IntPtr ptr)
        {
            int size = ShaderConductor.GetShaderConductorBlobSize(ptr);
            IntPtr ptrData = ShaderConductor.GetShaderConductorBlobData(ptr);
            byte[] bytes = new byte[size];
            Marshal.Copy(ptrData, bytes, 0, size);
            return bytes;
        }
    }
}