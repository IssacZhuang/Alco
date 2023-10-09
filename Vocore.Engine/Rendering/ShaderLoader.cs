using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{

    public static class ShaderLoader
    {
        public const string EntryPointVertex = "main";
        public const string EntryPointFragment = "main";


        public static ShaderDescription PreprocessShader(byte[] shaderText, ShaderStages stage, string entryPoint = "main")
        {
            return new ShaderDescription(stage, shaderText, entryPoint);
        }

        public static ShaderDescription PreprocessShader(string shaderText, ShaderStages stage, string entryPoint = "main")
        {
            return PreprocessShader(Encoding.UTF8.GetBytes(shaderText), stage, entryPoint);
        }

        public static ShaderDescription PreprocessVertexShader(byte[] shaderText, string entryPoint = EntryPointVertex)
        {
            return PreprocessShader(shaderText, ShaderStages.Vertex, entryPoint);
        }

        public static ShaderDescription PreprocessVertexShader(string shaderText, string entryPoint = EntryPointVertex)
        {
            return PreprocessShader(shaderText, ShaderStages.Vertex, entryPoint);
        }

        public static ShaderDescription PreprocessFragmentShader(byte[] shaderText, string entryPoint = EntryPointFragment)
        {
            return PreprocessShader(shaderText, ShaderStages.Fragment, entryPoint);
        }

        public static ShaderDescription PreprocessFragmentShader(string shaderText, string entryPoint = EntryPointFragment)
        {
            return PreprocessShader(shaderText, ShaderStages.Fragment, entryPoint);
        }

        public static Pipeline CreateShaderPipline(GraphicsDevice graphicsDevice, Shader[] shaders)
        {
            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription();
            pipelineDescription.BlendState = BlendStateDescription.SingleOverrideBlend;
            pipelineDescription.DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.LessEqual);

            pipelineDescription.RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.None,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false);

            pipelineDescription.PrimitiveTopology = PrimitiveTopology.TriangleList;
            pipelineDescription.ResourceLayouts = Array.Empty<ResourceLayout>();
            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: new VertexLayoutDescription[] { Vertex.Layout },
                shaders: shaders
                );
            pipelineDescription.Outputs = graphicsDevice.SwapchainFramebuffer.OutputDescription;
            return graphicsDevice.ResourceFactory.CreateGraphicsPipeline(pipelineDescription);
        }

        public static Shader[] LoadGLSL(GraphicsDevice graphicsDevice, ShaderDescription vert, ShaderDescription frag)
        {
            return graphicsDevice.ResourceFactory.CreateFromSpirv(vert, frag);
        }

        public static Shader[] LoadGLSL(GraphicsDevice graphicsDevice, byte[] vert, byte[] frag)
        {
            return LoadGLSL(graphicsDevice, PreprocessVertexShader(vert), PreprocessFragmentShader(frag));
        }
        public static Shader[] LoadGLSL(GraphicsDevice graphicsDevice, string vert, string frag)
        {
            return LoadGLSL(graphicsDevice, PreprocessVertexShader(vert), PreprocessFragmentShader(frag));
        }

        public static Pipeline CreateShaderPiplineFromGLSL(GraphicsDevice graphicsDevice, byte[] vert, byte[] frag)
        {
            return CreateShaderPipline(graphicsDevice, LoadGLSL(graphicsDevice, vert, frag));
        }

        public static Shader[] LoadHLSL(GraphicsDevice graphicsDevice, byte[] vert, byte[] frag)
        {
            Shader vertexShader = graphicsDevice.ResourceFactory.CreateShader(PreprocessVertexShader(vert));
            Shader fragmentShader = graphicsDevice.ResourceFactory.CreateShader(PreprocessFragmentShader(frag));
            return new Shader[2] { vertexShader, fragmentShader };
        }

        public static Shader[] LoadHLSL(GraphicsDevice graphicsDevice, string vert, string frag)
        {
            Shader vertexShader = graphicsDevice.ResourceFactory.CreateShader(PreprocessVertexShader(vert));
            Shader fragmentShader = graphicsDevice.ResourceFactory.CreateShader(PreprocessFragmentShader(frag));
            return new Shader[2] { vertexShader, fragmentShader };
        }

        public static Pipeline CreateShaderPiplineFromHLSL(GraphicsDevice graphicsDevice, byte[] vert, byte[] frag)
        {
            return CreateShaderPipline(graphicsDevice, LoadHLSL(graphicsDevice, vert, frag));
        }
    }
}

