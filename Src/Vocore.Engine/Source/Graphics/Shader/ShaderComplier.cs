using System;

using System.Text;
using System.Text.RegularExpressions;


using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{
    public class ShaderComplier
    {
        public const string Regex_Includes = @"#include\s+""(?<filename>[^""]+)""";
        public const string Format_LineInculde = "#line {0} \"{1}\"";

        public static readonly ShaderMacroDefine MacroD3D11 = new ShaderMacroDefine("BACKEND_D3D11", "TRUE");
        public static readonly ShaderMacroDefine MacroD3D12 = new ShaderMacroDefine("BACKEND_D3D12", "TRUE");
        public static readonly ShaderMacroDefine MacroOpenGL = new ShaderMacroDefine("BACKEND_OPENGL", "TRUE");
        public static readonly ShaderMacroDefine MacroOpenGLES = new ShaderMacroDefine("BACKEND_OPENGLES", "TRUE");
        public static readonly ShaderMacroDefine MacroVulkan = new ShaderMacroDefine("BACKEND_VULKAN", "TRUE");
        public static readonly ShaderMacroDefine MacroMetal = new ShaderMacroDefine("BACKEND_METAL", "TRUE");

        private readonly GraphicsDevice _device;
        private readonly ResourceFactory _factory;
        private readonly IVirtualDirectory? _sourceLibs;
        public ShaderComplier(GraphicsDevice device, IVirtualDirectory? sourceLibs = null)
        {
            _device = device;
            _factory = device.ResourceFactory;
            _sourceLibs = sourceLibs;
        }

        /// <summary>
        /// Complie a HLSL shader to specified graphics backend
        /// </summary>
        public Shader Complie(ShaderComplieDescription input)
        {
            List<ShaderMacroDefine> macroList = new List<ShaderMacroDefine>(){
                GetBackendMacro(_device.BackendType)
            };
            if (input.Macros != null)
            {
                macroList.AddRange(input.Macros);
            }
            string shaderCode = ProcessMacro(input.ShaderText, macroList);
            shaderCode = ProcessInclude(shaderCode, input.Filename);
            CrossComplieResult result = HlslCrossComplier.ComplieGraphicsShader(shaderCode, _device.BackendType, input.VertexEntry, input.FragmentEntry);

            ShaderDescription vertexShaderDescription = result.vertex;
            ShaderDescription fragmentShaderDescription = result.fragment;

            ShaderAnalyzer analyseResult = new ShaderAnalyzer(input.ShaderText);

            SpirvReflection reflection = result.reflection;

            VertexLayoutDescription[] _vertexLayouts = new VertexLayoutDescription[] { new VertexLayoutDescription(reflection.VertexElements) };

            Veldrid.Shader[] shaders = new Veldrid.Shader[2];

            if (_device.BackendType == GraphicsBackend.Vulkan)
            {
                shaders = _factory.CreateFromSpirv(vertexShaderDescription, fragmentShaderDescription);
            }
            else
            {
                shaders[0] = _factory.CreateShader(vertexShaderDescription);
                shaders[1] = _factory.CreateShader(fragmentShaderDescription);
            }

            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription();
            pipelineDescription.BlendState = analyseResult.GetBlendState();
            pipelineDescription.DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: analyseResult.GetDepthTestEnable(),
                depthWriteEnabled: analyseResult.GetDepthWriteEnable(),
                comparisonKind: ComparisonKind.LessEqual);

            pipelineDescription.RasterizerState = new RasterizerStateDescription(
                cullMode: analyseResult.GetCullMode(),
                fillMode: analyseResult.GetFillMode(),
                frontFace: FrontFace.Clockwise,
                depthClipEnabled: analyseResult.GetDepthClipEnable(),
                scissorTestEnabled: analyseResult.GetScissorTestEnable());

            pipelineDescription.PrimitiveTopology = analyseResult.GetTopologyPrimitive();

            ResourceLayoutDescription[] resourceLayoutsDesc;

            if (_device.BackendType == GraphicsBackend.OpenGL || _device.BackendType == GraphicsBackend.OpenGLES)
            {
                resourceLayoutsDesc = FixOpenGLBufferName(reflection.ResourceLayouts);
            }
            else
            {
                resourceLayoutsDesc = reflection.ResourceLayouts;
            }


            ResourceLayout[] resourceLayouts = new ResourceLayout[reflection.ResourceLayouts.Length];
            for (int i = 0; i < reflection.ResourceLayouts.Length; i++)
            {
                resourceLayouts[i] = _factory.CreateResourceLayout(resourceLayoutsDesc[i]);
            }
            pipelineDescription.ResourceLayouts = resourceLayouts;
            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: _vertexLayouts,
                shaders: shaders
                );

            if (input.OutputDescription.HasValue)
            {
                pipelineDescription.Outputs = input.OutputDescription.Value;
            }
            else
            {
                pipelineDescription.Outputs = _device.SwapchainFramebuffer.OutputDescription;
            }

            Pipeline pipline = _factory.CreateGraphicsPipeline(pipelineDescription);

            return new Shader(input.Filename, pipline, reflection);
        }

        public string ProcessMacro(string shaderText, ShaderMacroDefine[] macros)
        {
            return ShaderMacroDefine.BuildMacroString(macros) + shaderText;
        }

        public string ProcessMacro(string shaderText, IList<ShaderMacroDefine> macros)
        {
            return ShaderMacroDefine.BuildMacroString(macros) + shaderText;
        }

        /// <summary>
        /// Preprocess the #include statement in shader text
        /// </summary>
        public string ProcessInclude(string shaderText, string filename = "Unknow")
        {
            if (_sourceLibs == null)
            {
                return shaderText;
            }
            
            StringBuilder sb = new StringBuilder();
            string[] lines = shaderText.Split('\n');
            int lineIndex = 0;
            foreach (var line in lines)
            {
                Match match = Regex.Match(line, Regex_Includes);
                if (match.Success)
                {
                    string includeFilename = match.Groups["filename"].Value;
                    if (_sourceLibs.TryGetData(includeFilename, out var includeData))
                    {
                        sb.AppendLine(string.Format(Format_LineInculde, 1, includeFilename));
                        sb.AppendLine(ProcessInclude(Encoding.UTF8.GetString(includeData), includeFilename));
                        sb.AppendLine(string.Format(Format_LineInculde, lineIndex + 2, filename));
                    }
                    else
                    {
                        throw new ShaderCompilationException($"Include file {includeFilename} not found");
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

        private ShaderMacroDefine GetBackendMacro(GraphicsBackend backend)
        {
            switch (backend)
            {
                case GraphicsBackend.Direct3D11:
                    return MacroD3D11;
                // currently not supported dx12
                // case GraphicsBackend.Direct3D12:
                //     return MacroD3D12;
                case GraphicsBackend.OpenGL:
                    return MacroOpenGL;
                case GraphicsBackend.OpenGLES:
                    return MacroOpenGLES;
                case GraphicsBackend.Vulkan:
                    return MacroVulkan;
                case GraphicsBackend.Metal:
                    return MacroMetal;
                default:
                    throw new NotSupportedException($"Backend {backend} not supported");
            }
        }

        private static ResourceLayoutDescription[] FixOpenGLBufferName(ResourceLayoutDescription[] layouts)
        {
            ResourceLayoutDescription[] result = new ResourceLayoutDescription[layouts.Length];
            for (int i = 0; i < layouts.Length; i++)
            {
                result[i].Elements = new ResourceLayoutElementDescription[layouts[i].Elements.Length];
                for (int j = 0; j < layouts[i].Elements.Length; j++)
                {
                    //copy the element
                    result[i].Elements[j] = layouts[i].Elements[j];
                    //fix name
                    result[i].Elements[j].Name = FixOpenGLBufferName(result[i].Elements[j].Name);
                }
            }
            return result;
        }


        private static string FixOpenGLBufferName(string name)
        {
            //replace . to _
            return name.Replace('.', '_');
        }


    }
}