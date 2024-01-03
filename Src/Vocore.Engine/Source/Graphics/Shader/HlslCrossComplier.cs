using System;
using System.Runtime.InteropServices;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;
using Vortice.Dxc;

namespace Vocore.Engine
{
    public static class HlslCrossComplier
    {
        public const string CompliedEntry = "main";

        public static readonly byte[] SpirvHeader = new byte[] { 0x03, 0x02, 0x23, 0x07 };

        public static CrossComplieResult ComplieGraphicsShader(string hlslCode, string filename, GraphicsBackend backend, string entryVertex = "VS", string entryFragment = "PS", ShaderMacroDefine[]? macros = null)
        {
            DxcDefine[] dxcMacros = ShaderMacroDefine.ToDxcMacro(macros);
            byte[] vertexSpirv = ConvetHlslToSpirv(hlslCode, filename, entryVertex, DxcShaderStage.Vertex, dxcMacros);
            byte[] fragmentSpirv = ConvetHlslToSpirv(hlslCode, filename, entryFragment, DxcShaderStage.Pixel, dxcMacros);

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
                VertexFragmentCompilationResult spirvToShaderResult = SpirvCompilation.CompileVertexFragment(vertexSpirv, fragmentSpirv, CrossCompileTarget.GLSL);
                reflection = spirvToShaderResult.Reflection;

                result = new CrossComplieResult(vertexSpirv, CompliedEntry, fragmentSpirv, CompliedEntry, reflection);
            }

            return result;
        }



        public static byte[] ConvetHlslToSpirv(string hlslCode, string filename, string entry, DxcShaderStage stage, DxcDefine[]? defines = null)
        {
            IDxcResult result = DxcCompiler.Compile(stage, hlslCode, entry, new DxcCompilerOptions()
            {
                GenerateSpirv = true,
            }, filename, defines);

            if (result.GetStatus() != SharpGen.Runtime.Result.Ok)
            {

                throw new ShaderCompilationException(result.GetErrors());
            }

            return result.GetObjectBytecodeArray();
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
    }
}