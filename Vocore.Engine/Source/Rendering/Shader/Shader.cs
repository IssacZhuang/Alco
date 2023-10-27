using System;
using System.Runtime.CompilerServices;
using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{
    public class Shader : IDisposable
    {
        private const string DefaultEntryPoint = "main";
        private Pipeline _pipeline;
        private SpirvReflection _reflection;
        private VertexLayoutDescription[] _vertexLayouts;
        private ResourceLayout[] _resourceLayouts;
        private GraphicsPipelineDescription _pipelineDescription;
        private ShaderAnalyseResult _analyseResult;
        private string _name;

        public Pipeline Pipeline
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _pipeline;
        }

        public Shader(GraphicsDevice device, string shaderText, string filename = "Unknow")
        {
            _name = filename;
            try
            {
                ResourceFactory factory = device.ResourceFactory;

                _analyseResult = new ShaderAnalyseResult(shaderText);

                ShaderByteCode vertexByteCode = ShaderComplier.ComplieVertexShaderToSpirv(shaderText, filename);
                ShaderByteCode fragmentByteCode = ShaderComplier.ComplieFragmentShaderToSpirv(shaderText, filename);
                ShaderDescription vertexShaderDescription = new ShaderDescription(ShaderStages.Vertex, vertexByteCode.Bytes, DefaultEntryPoint);
                ShaderDescription fragmentShaderDescription = new ShaderDescription(ShaderStages.Fragment, fragmentByteCode.Bytes, DefaultEntryPoint);

                _reflection = ShaderComplier.GetShaderReflection(device, shaderText, filename);

                if (_analyseResult.HasInstanceBuffer)
                {
                    VertexElementDescription[] instanceElements = new VertexElementDescription[_analyseResult.IntancedElementCount];
                    for (int i = 0; i < _analyseResult.IntancedElementCount; i++)
                    {
                        instanceElements[i] = _reflection.VertexElements[_analyseResult.instanceBufferStartIndex + i];
                    }
                    VertexLayoutDescription instanceLayout = new VertexLayoutDescription(instanceElements)
                    {
                        InstanceStepRate = 1
                    };
                    int vertexElementCount = _reflection.VertexElements.Length - _analyseResult.IntancedElementCount;
                    VertexElementDescription[] vertexElements = new VertexElementDescription[vertexElementCount];
                    for (int i = 0; i < _analyseResult.instanceBufferStartIndex; i++)
                    {
                        vertexElements[i] = _reflection.VertexElements[i];
                    }
                    for (int i = _analyseResult.instanceBufferEndIndex + 1; i < _reflection.VertexElements.Length; i++)
                    {
                        vertexElements[i - _analyseResult.IntancedElementCount] = _reflection.VertexElements[i];
                    }
                    VertexLayoutDescription vertexLayout = new VertexLayoutDescription(vertexElements);
                    _vertexLayouts = new VertexLayoutDescription[] { vertexLayout, instanceLayout };
                }
                else
                {
                    VertexLayoutDescription vertexLayout = new VertexLayoutDescription(_reflection.VertexElements);
                    _vertexLayouts = new VertexLayoutDescription[] { vertexLayout };
                }


                Veldrid.Shader[] shaders = factory.CreateFromSpirv(vertexShaderDescription, fragmentShaderDescription);

                _pipelineDescription = new GraphicsPipelineDescription();
                _pipelineDescription.BlendState = _analyseResult.GetBlendState();
                _pipelineDescription.DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: _analyseResult.GetDepthTestEnable(),
                    depthWriteEnabled: _analyseResult.GetDepthWriteEnable(),
                    comparisonKind: ComparisonKind.LessEqual);

                _pipelineDescription.RasterizerState = new RasterizerStateDescription(
                    cullMode: _analyseResult.GetCullMode(),
                    fillMode: _analyseResult.GetFillMode(),
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: _analyseResult.GetDepthClipEnable(),
                    scissorTestEnabled: _analyseResult.GetScissorTestEnable());

                _pipelineDescription.PrimitiveTopology = _analyseResult.GetTopologyPrimitive();
                _resourceLayouts = new ResourceLayout[_reflection.ResourceLayouts.Length];
                for (int i = 0; i < _reflection.ResourceLayouts.Length; i++)
                {
                    _resourceLayouts[i] = factory.CreateResourceLayout(_reflection.ResourceLayouts[i]);
                }
                _pipelineDescription.ResourceLayouts = _resourceLayouts;
                _pipelineDescription.ShaderSet = new ShaderSetDescription(
                    vertexLayouts: _vertexLayouts,
                    shaders: shaders
                    );
                _pipelineDescription.Outputs = device.SwapchainFramebuffer.OutputDescription;
                _pipeline = factory.CreateGraphicsPipeline(_pipelineDescription);
            }
            catch (Exception e)
            {
                throw new Exception($"{e.Message} \nCode: \n{shaderText} \n{e.StackTrace}");
            }
        }

        public void Dispose()
        {
            _pipeline.Dispose();
        }

        public string GetReflectionInfo()
        {
            string result = $"Shader [{_name}]\n";
            result += "Vertex Elements:\n";
            for(int i = 0; i < _vertexLayouts[0].Elements.Length; i++)
            {
                result += $"{i} {_vertexLayouts[0].Elements[i].Name}\n";
            }
            result += "Instance Elements:\n";
            if (_analyseResult.HasInstanceBuffer)
            {
                for (int i = 0; i < _vertexLayouts[1].Elements.Length; i++)
                {
                    result += $"{i} {_vertexLayouts[1].Elements[i].Name}\n";
                }
            }
            result += "Resource Layouts:\n";
            for(int i = 0; i < _reflection.ResourceLayouts.Length; i++)
            {
                for(int j = 0; j < _reflection.ResourceLayouts[i].Elements.Length; j++)
                {
                    result += $"{i} {j} {_reflection.ResourceLayouts[i].Elements[j].Name}\n";
                }
            }
            return result;
        }
    }
}

