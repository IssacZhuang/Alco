using System;
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
        
        public static readonly MacroDefinition MacroVertex = new MacroDefinition(MacroStageVertex, GLSL_True);
        public static readonly MacroDefinition MacroFragment = new MacroDefinition(MacroStageFragment, GLSL_True);
        public static readonly GlslCompileOptions OptionVertex = new GlslCompileOptions(true, MacroVertex);
        public static readonly GlslCompileOptions OptionFragment = new GlslCompileOptions(true, MacroFragment);


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

        public static Pipeline CreateShaderPiplineFromGLSL(GraphicsDevice graphicsDevice, string shaderText, string filename = "unknown_shader")
        {
            return CreateShaderPipline(graphicsDevice, shaderText, filename);
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
        

        private static Pipeline CreateShaderPipline(GraphicsDevice graphicsDevice, string shaderText, string filename)
        {
            ResourceFactory factory = graphicsDevice.ResourceFactory;

            ShaderAnalyseResult analyseResult = new ShaderAnalyseResult(shaderText);
            
            ShaderByteCode vertexByteCode = ComplieVertexShaderToSpirv(shaderText, filename);
            ShaderByteCode fragmentByteCode = ComplieFragmentShaderToSpirv(shaderText, filename);
            ShaderDescription vertexShaderDescription = new ShaderDescription(ShaderStages.Vertex, vertexByteCode.Bytes, DefaultEntryPoint);
            ShaderDescription fragmentShaderDescription = new ShaderDescription(ShaderStages.Fragment, fragmentByteCode.Bytes, DefaultEntryPoint);

            SpirvReflection reflection = GetShaderReflection(graphicsDevice, shaderText, filename);

            VertexLayoutDescription[] vertexDesc;

            if (analyseResult.HasInstanceBuffer)
            {
                VertexElementDescription[] instanceElements = new VertexElementDescription[analyseResult.IntancedElementCount];
                for (int i = 0; i < analyseResult.IntancedElementCount; i++)
                {
                    instanceElements[i] = reflection.VertexElements[analyseResult.instanceBufferStartIndex + i];
                }
                VertexLayoutDescription instanceLayout = new VertexLayoutDescription(instanceElements)
                {
                    InstanceStepRate = 1
                };
                int vertexElementCount = reflection.VertexElements.Length - analyseResult.IntancedElementCount;
                VertexElementDescription[] vertexElements = new VertexElementDescription[vertexElementCount];
                for (int i = 0; i < analyseResult.instanceBufferStartIndex; i++)
                {
                    vertexElements[i] = reflection.VertexElements[i];
                }
                for (int i = analyseResult.instanceBufferEndIndex + 1; i < reflection.VertexElements.Length; i++)
                {
                    vertexElements[i - analyseResult.IntancedElementCount] = reflection.VertexElements[i];
                }
                VertexLayoutDescription vertexLayout = new VertexLayoutDescription(vertexElements);
                vertexDesc = new VertexLayoutDescription[] { vertexLayout, instanceLayout };
            }
            else
            {
                VertexLayoutDescription vertexLayout = new VertexLayoutDescription(reflection.VertexElements);
                vertexDesc = new VertexLayoutDescription[] { vertexLayout };
            }


            Veldrid.Shader[] shaders = factory.CreateFromSpirv(vertexShaderDescription, fragmentShaderDescription);
            
            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription();
            pipelineDescription.BlendState = BlendStateDescription.SingleOverrideBlend;
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
            var resourceLayoutCamera = graphicsDevice.ResourceFactory.CreateResourceLayout(BufferLayout.Camera);
            var resourceLayoutTransform = graphicsDevice.ResourceFactory.CreateResourceLayout(BufferLayout.Transform);
            pipelineDescription.ResourceLayouts = new ResourceLayout[] {
                resourceLayoutCamera,
                resourceLayoutTransform
              };
            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: vertexDesc,
                shaders: shaders
                );
            pipelineDescription.Outputs = graphicsDevice.SwapchainFramebuffer.OutputDescription;
            return graphicsDevice.ResourceFactory.CreateGraphicsPipeline(pipelineDescription);
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