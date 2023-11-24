using System;
using System.Runtime.InteropServices;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

using Vocore.Engine.NativeBinding;

namespace Vocore.Engine
{
    public static class HlslCrossComplier
    {
        public static readonly ShaderConductor.TargetDesc TargetD3d12 = new ShaderConductor.TargetDesc
        {
            language = ShaderConductor.ShadingLanguage.Dxil,
            version = null,
        };

        public static readonly ShaderConductor.TargetDesc TargetD3d11 = new ShaderConductor.TargetDesc
        {
            language = ShaderConductor.ShadingLanguage.Hlsl,
            version = null,
        };

        public static readonly ShaderConductor.TargetDesc TargetDxil = new ShaderConductor.TargetDesc
        {
            language = ShaderConductor.ShadingLanguage.Dxil,
            version = null,
        };

        public static readonly ShaderConductor.TargetDesc TargetVulkan = new ShaderConductor.TargetDesc
        {
            language = ShaderConductor.ShadingLanguage.SpirV,
            version = null,
        };

        public static readonly ShaderConductor.TargetDesc TargetSpirv = new ShaderConductor.TargetDesc
        {
            language = ShaderConductor.ShadingLanguage.SpirV,
            version = "1.3",
        };

        public static readonly ShaderConductor.TargetDesc TargetOpenGL = new ShaderConductor.TargetDesc
        {
            language = ShaderConductor.ShadingLanguage.Glsl,
            version = null,
        };

        public static readonly ShaderConductor.TargetDesc TargetOpenGLES = new ShaderConductor.TargetDesc
        {
            language = ShaderConductor.ShadingLanguage.Essl,
            version = "300",
        };

        public static readonly ShaderConductor.TargetDesc TargetMetal = new ShaderConductor.TargetDesc
        {
            language = ShaderConductor.ShadingLanguage.Msl_macOS,
            version = "",
        };

        public const string CompliedEntry = "main";


        public static readonly byte[] SpirvHeader = new byte[] { 0x03, 0x02, 0x23, 0x07 };

        public static CrossComplieResult ComplieGraphicsShader(string hlslCode, GraphicsBackend backend, string entryVertex = "VS", string entryFragment = "PS")
        {
            byte[] vertexSpirv = ConvertHlsl(hlslCode, entryVertex, ShaderConductor.ShaderStage.VertexShader, TargetSpirv);
            byte[] fragmentSpirv = ConvertHlsl(hlslCode, entryFragment, ShaderConductor.ShaderStage.PixelShader, TargetSpirv);

            SpirvReflection reflection;
            CrossComplieResult result;

            if (backend == GraphicsBackend.Vulkan)
            {
                //the spirv-cross only get reflection for vulkan
                VertexFragmentCompilationResult spirvToShaderResult = SpirvCompilation.CompileVertexFragment(vertexSpirv, fragmentSpirv, CrossCompileTarget.GLSL);
                reflection = spirvToShaderResult.Reflection;

                result = new CrossComplieResult(vertexSpirv, entryVertex, fragmentSpirv, entryFragment, reflection);
            }
            else
            {
                CrossCompileTarget target = GetCompileTarget(backend);
                VertexFragmentCompilationResult spirvToShaderResult = SpirvCompilation.CompileVertexFragment(vertexSpirv, fragmentSpirv, target);
                reflection = spirvToShaderResult.Reflection;
                result = new CrossComplieResult(
                    Encoding.UTF8.GetBytes(spirvToShaderResult.VertexShader),
                    CompliedEntry,
                    Encoding.UTF8.GetBytes(spirvToShaderResult.FragmentShader),
                    CompliedEntry,
                    reflection
                    );
            }

            return result;
        }

        public static byte[] ConvertHlsl(string hlslCode, string entryPoint, ShaderConductor.ShaderStage stage, ShaderConductor.TargetDesc target)
        {
            ShaderConductor.SourceDesc sourceDesc = new ShaderConductor.SourceDesc
            {
                source = hlslCode,
                entryPoint = entryPoint,
                stage = stage,
            };

            ShaderConductor.OptionsDesc optionsDesc = ShaderConductor.OptionsDesc.Default;

            ShaderConductor.Compile(ref sourceDesc, ref optionsDesc, ref target, out ShaderConductor.ResultDesc resultDesc);
            if (resultDesc.hasError)
            {
                throw new Exception(Marshal.PtrToStringAnsi(ShaderConductor.GetShaderConductorBlobData(resultDesc.errorWarningMsg)));
            }

            byte[] spirvBytes = ShaderBlobToBytes(resultDesc.target);
            ShaderConductor.DestroyShaderConductorBlob(resultDesc.target);
            return spirvBytes;
        }

        //fix glsl reflection to hls reflection
        private static SpirvReflection FixReflection(SpirvReflection reflection)
        {
            ResourceLayoutDescription realLayout = reflection.ResourceLayouts[0];
            ResourceLayoutElementDescription[] elements = realLayout.Elements;
            ResourceLayoutDescription[] layouts = new ResourceLayoutDescription[elements.Length];
            for (int i = 0; i < elements.Length; i++)
            {
                layouts[i] = CreateLayoutByElement(elements[i]);
            }
            return new SpirvReflection(reflection.VertexElements, layouts);
        }

        private static ResourceLayoutDescription CreateLayoutByElement(ResourceLayoutElementDescription element)
        {
            return new ResourceLayoutDescription(new ResourceLayoutElementDescription[] { element });
        }

        private static CrossCompileTarget GetCompileTarget(GraphicsBackend backend)
        {
            switch (backend)
            {
                case GraphicsBackend.Direct3D11:
                    return CrossCompileTarget.HLSL;
                case GraphicsBackend.Vulkan:
                    return CrossCompileTarget.GLSL;
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

        private static byte[] ShaderBlobToBytes(IntPtr ptr)
        {
            int size = ShaderConductor.GetShaderConductorBlobSize(ptr);
            IntPtr ptrData = ShaderConductor.GetShaderConductorBlobData(ptr);
            byte[] bytes = new byte[size];
            Marshal.Copy(ptrData, bytes, 0, size);
            return bytes;
        }
    }
}