using System;
using System.Text;
using System.Text.RegularExpressions;

using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{
    internal class EngineShaderContext
    {
        public const string MacroStageVertex = "VERTEX_SHADER";
        public const string MacroStageFragment = "FRAGMENT_SHADER";
        public const string MacroPlatformVulkan = "BACKEND_VULKAN";
        public const string MacroPlatformMetal = "BACKEND_METAL";
        public const string MacroPlatformDirect3D11 = "BACKEND_DIRECT3D11";
        public const string MacroPlatformOpenGL = "BACKEND_OPENGL";
        public const string MacroPlatformOpenGLES = "BACKEND_OPENGLES";
        public const string GLSL_True = "true";
        public const string GLSL_False = "false";
        public const string DefaultEntryPoint = "main";
        public const string Regex_Includes = @"#include\s+""(?<filename>[^""]+)""";
        public const string Format_LineInculde = "#line {0} \"{1}\"";

        public static readonly MacroDefinition MacroVertex = new MacroDefinition(MacroStageVertex, GLSL_True);
        public static readonly MacroDefinition MacroFragment = new MacroDefinition(MacroStageFragment, GLSL_True);
        public static readonly MacroDefinition MacroPlatformVulkanDef = new MacroDefinition(MacroPlatformVulkan, GLSL_True);
        public static readonly MacroDefinition MacroPlatformMetalDef = new MacroDefinition(MacroPlatformMetal, GLSL_True);
        public static readonly MacroDefinition MacroPlatformDirect3D11Def = new MacroDefinition(MacroPlatformDirect3D11, GLSL_True);
        public static readonly MacroDefinition MacroPlatformOpenGLDef = new MacroDefinition(MacroPlatformOpenGL, GLSL_True);
        public static readonly MacroDefinition MacroPlatformOpenGLESDef = new MacroDefinition(MacroPlatformOpenGLES, GLSL_True);
        
        private readonly Dictionary<string, Shader> _shaders = new Dictionary<string, Shader>();
        private readonly GraphicsDevice _device;
        public BaseVirtualDirectory SourceLibs { get; private set; }
        public BaseVirtualDirectory SourceGraphics { get; private set; }
        public BaseVirtualDirectory SourceCompute { get; private set; }

        public EngineShaderContext(GraphicsDevice device)
        {
            _device = device;

            SourceLibs = new BaseVirtualDirectory();
            SourceGraphics = new BaseVirtualDirectory();
            SourceCompute = new BaseVirtualDirectory();
        }

        public Shader? Get(string name)
        {
            if (_shaders.TryGetValue(name, out var shader))
            {
                return shader;
            }
            Log.Error($"Shader {name} not found in pool");
            return null;
        }

        public bool TryGet(string name, out Shader? shader)
        {
            if (_shaders.TryGetValue(name, out shader))
            {
                return true;
            }
            return false;
        }

        public void Add(string name, Shader shader)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if (_shaders.ContainsKey(name))
            {
                Log.Error($"Shader {name} already exists in pool");
                return;
            }
            _shaders.Add(name, shader);
        }


        public string ProcessInclude(string filename, string shaderText)
        {
            //insert including content and #line in the #include position
            StringBuilder sb = new StringBuilder();
            string[] lines = shaderText.Split('\n');
            int lineIndex = 0;
            foreach (var line in lines)
            {
                Match match = Regex.Match(line, Regex_Includes);
                if (match.Success)
                {
                    string includeFilename = match.Groups["filename"].Value;
                    if (SourceLibs.TryGetData(includeFilename, out var includeData))
                    {
                        sb.AppendLine(string.Format(Format_LineInculde, 1, includeFilename));
                        sb.AppendLine(ProcessInclude(includeFilename, Encoding.UTF8.GetString(includeData)));
                        sb.AppendLine(string.Format(Format_LineInculde, lineIndex + 2, filename));
                    }
                    else
                    {
                        Log.Error($"Include file {includeFilename} not found");
                    }
                }
                else
                {
                    sb.AppendLine(line);
                }
                lineIndex++;
            }
            return sb.ToString();
        }


        

        private static CrossCompileTarget GetCompilationTarget(GraphicsBackend backend)
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

        public static MacroDefinition GetBackendMacro(GraphicsBackend backend)
        {
            switch (backend)
            {
                case GraphicsBackend.Direct3D11:
                    return MacroPlatformDirect3D11Def;
                case GraphicsBackend.OpenGL:
                    return MacroPlatformOpenGLDef;
                case GraphicsBackend.Metal:
                    return MacroPlatformMetalDef;
                case GraphicsBackend.OpenGLES:
                    return MacroPlatformOpenGLESDef;
                case GraphicsBackend.Vulkan:
                    return MacroPlatformVulkanDef;
                default:
                    throw new SpirvCompilationException($"Invalid GraphicsBackend: {backend}");
            }
        }
    }
}