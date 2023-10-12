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


        

        private static Pipeline CreateShaderPipline(GraphicsDevice graphicsDevice, string shaderText, string filename)
        {
            ResourceFactory factory = graphicsDevice.ResourceFactory;

            CrossCompileTarget compilationTarget = GetCompilationTarget(factory.BackendType);
            

            ShaderAnalyseResult analyseResult = new ShaderAnalyseResult(shaderText);
            
            ShaderByteCode vertexByteCode = ComplieVertexShaderToSpirv(shaderText, filename);
            ShaderByteCode fragmentByteCode = ComplieFragmentShaderToSpirv(shaderText, filename);
            ShaderDescription vertexShaderDescription = new ShaderDescription(ShaderStages.Vertex, vertexByteCode.Bytes, DefaultEntryPoint);
            ShaderDescription fragmentShaderDescription = new ShaderDescription(ShaderStages.Fragment, fragmentByteCode.Bytes, DefaultEntryPoint);

            VertexFragmentCompilationResult vertexFragmentCompilationResult = SpirvCompilation.CompileVertexFragment(vertexShaderDescription.ShaderBytes, fragmentShaderDescription.ShaderBytes, compilationTarget, new CrossCompileOptions{
                NormalizeResourceNames = false,
            });
            Log.Info($"Vertex Shader: {vertexFragmentCompilationResult.VertexShader}");
            Log.Info($"Fragment Shader: {vertexFragmentCompilationResult.FragmentShader}");

            Shader[] shaders = factory.CreateFromSpirv(vertexShaderDescription, fragmentShaderDescription);
            
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

            pipelineDescription.PrimitiveTopology = PrimitiveTopology.TriangleList;
            var resourceLayoutCamera = graphicsDevice.ResourceFactory.CreateResourceLayout(BufferLayout.Camera);
            var resourceLayoutTransform = graphicsDevice.ResourceFactory.CreateResourceLayout(BufferLayout.Transform);
            pipelineDescription.ResourceLayouts = new ResourceLayout[] {
                resourceLayoutCamera,
                resourceLayoutTransform
              };
            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: new VertexLayoutDescription[] { Vertex.Layout },
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
                _ => throw new SpirvCompilationException($"Invalid GraphicsBackend: {backend}"),
            };
        }
    }
}