using System;
using System.Text;
using System.Text.RegularExpressions;


using Veldrid;
using Veldrid.SPIRV;
using Vocore.ShaderCross;

namespace Vocore.Engine
{
    public class ShaderComplier
    {
        public const string Regex_Includes = @"#include\s+""(?<filename>[^""]+)""";
        public const string Format_LineInculde = "#line {0} \"{1}\"";


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
        public Shader Complie(string shaderText, string filename = "Unknow", string vertexEntry = "VS", string fragmentEntry = "PS")
        {
            string shaderCode = ProcessInclude(shaderText, filename);
            CrossComplieResult result = HlslCrossComplier.ComplieGraphicsShader(shaderCode, _device.BackendType, vertexEntry, fragmentEntry);

            ShaderDescription vertexShaderDescription = result.vertex;
            ShaderDescription fragmentShaderDescription = result.fragment;

            ShaderAnalyseResult analyseResult = new ShaderAnalyseResult(shaderText);

            SpirvReflection reflection = result.reflection;

            VertexLayoutDescription[] _vertexLayouts = new VertexLayoutDescription[] { new VertexLayoutDescription(reflection.VertexElements) };

            Veldrid.Shader[] shaders = new Veldrid.Shader[2];
            shaders[0] = _factory.CreateShader(vertexShaderDescription);
            shaders[1] = _factory.CreateShader(fragmentShaderDescription);

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

            ResourceLayout[] resourceLayouts = new ResourceLayout[reflection.ResourceLayouts.Length];
            for (int i = 0; i < reflection.ResourceLayouts.Length; i++)
            {
                resourceLayouts[i] = _factory.CreateResourceLayout(reflection.ResourceLayouts[i]);
            }
            pipelineDescription.ResourceLayouts = resourceLayouts;
            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: _vertexLayouts,
                shaders: shaders
                );
            pipelineDescription.Outputs = _device.SwapchainFramebuffer.OutputDescription;
            Pipeline pipline = _factory.CreateGraphicsPipeline(pipelineDescription);

            return new Shader(filename, pipline, reflection);
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


    }
}