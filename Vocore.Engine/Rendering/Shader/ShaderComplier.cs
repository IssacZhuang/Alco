using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;


using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{
    public static class ShaderComplier
    {
        public const string MacroStageVertex = "VERTEX_SHADER";
        public const string MacroStageFragment = "FRAGMENT_SHADER";
        public const string GLSL_True = "true";
        public const string GLSL_False = "false";
        public const string DefaultEntryPoint = "main";
        public const string Regex_Includes = @"#include\s+""(?<filename>[^""]+)""";
        public const string Format_LineInculde = "#line {0} \"{1}\"";
        
        public static readonly MacroDefinition MacroVertex = new MacroDefinition(MacroStageVertex, GLSL_True);
        public static readonly MacroDefinition MacroFragment = new MacroDefinition(MacroStageFragment, GLSL_True);
        public static readonly GlslCompileOptions OptionVertex = new GlslCompileOptions(true, MacroVertex);
        public static readonly GlslCompileOptions OptionFragment = new GlslCompileOptions(true, MacroFragment);

        public static string ProcessInclude(string shaderText, string filename, IVirtualDirectory shaderTextSource)
        {
            StringBuilder builder = new StringBuilder();
            int line = 1;
            foreach (Match match in Regex.Matches(shaderText, Regex_Includes))
            {
                string includeFilename = match.Groups["filename"].Value;
                if (shaderTextSource.TryGetData(includeFilename, out byte[] data))
                {
                    builder.AppendLine(string.Format(Format_LineInculde, line, filename));
                    builder.AppendLine(ProcessInclude(Encoding.UTF8.GetString(data), includeFilename, shaderTextSource));
                }
                else
                {
                    throw new Exception($"Can't find include file: {includeFilename} in {filename} line {line}");
                }
                line++;
            }
            builder.AppendLine(string.Format(Format_LineInculde, line, filename));
            builder.Append(shaderText);
            return builder.ToString();
        }

        public static ShaderByteCode ComplieVertexShaderToSpirv(string shaderText, string filename)
        {
            return ComplieGlslToSpirv(shaderText, filename, ShaderStages.Vertex, OptionVertex);
        }

        public static ShaderByteCode ComplieFragmentShaderToSpirv(string shaderText, string filename)
        {
            return ComplieGlslToSpirv(shaderText, filename, ShaderStages.Fragment, OptionFragment);
        }
        public static ShaderByteCode ComplieGlslToSpirv(string shaderText, string filename, ShaderStages stage, GlslCompileOptions option)
        {
            SpirvCompilationResult result = SpirvCompilation.CompileGlslToSpirv(shaderText, filename, stage, option);
            return new ShaderByteCode(result, filename);
        }

        public static SpirvReflection GetShaderReflection(GraphicsDevice graphicsDevice, string shaderText, string filename)
        {
            ShaderByteCode vertexByteCode = ComplieVertexShaderToSpirv(shaderText, filename);
            ShaderByteCode fragmentByteCode = ComplieFragmentShaderToSpirv(shaderText, filename);
            VertexFragmentCompilationResult vertexFragmentCompilationResult = SpirvCompilation.CompileVertexFragment(vertexByteCode.Bytes, fragmentByteCode.Bytes, GetCompilationTarget(graphicsDevice.BackendType), new CrossCompileOptions
            {
                NormalizeResourceNames = false,
            });
            return vertexFragmentCompilationResult.Reflection;
        }

        public static CrossCompileTarget GetCompilationTarget(GraphicsBackend backend)
        {
            return backend switch
            {
                GraphicsBackend.Direct3D11 => CrossCompileTarget.HLSL,
                GraphicsBackend.OpenGL => CrossCompileTarget.GLSL,
                GraphicsBackend.Metal => CrossCompileTarget.MSL,
                GraphicsBackend.OpenGLES => CrossCompileTarget.ESSL,
                GraphicsBackend.Vulkan => CrossCompileTarget.GLSL,
                _ => throw new SpirvCompilationException($"Invalid GraphicsBackend: {backend}"),
            };
        }
    }
}