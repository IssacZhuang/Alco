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
            return new ShaderDescription(stage, Encoding.UTF8.GetBytes(shaderText), entryPoint);
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

        public static Shader[] LoadShaders(GraphicsDevice graphicsDevice, ShaderDescription vert, ShaderDescription frag)
        {
            return graphicsDevice.ResourceFactory.CreateFromSpirv(vert, frag);
        }

        public static Shader[] LoadShaders(GraphicsDevice graphicsDevice, byte[] vert, byte[] frag)
        {
            return LoadShaders(graphicsDevice, PreprocessVertexShader(vert), PreprocessFragmentShader(frag));
        }
        public static Shader[] LoadShaders(GraphicsDevice graphicsDevice, string vert, string frag)
        {
            return LoadShaders(graphicsDevice, PreprocessVertexShader(vert), PreprocessFragmentShader(frag));
        }

        public static Shader LoadHLSLShader(GraphicsDevice graphicsDevice, byte[] text)
        {
            return graphicsDevice.ResourceFactory.CreateShader(PreprocessVertexShader(text));
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
                cullMode: FaceCullMode.Back,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false);

            pipelineDescription.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
            pipelineDescription.ResourceLayouts = Array.Empty<ResourceLayout>();
            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: new VertexLayoutDescription[] { Vertex.Layout },
                shaders: shaders);
            pipelineDescription.Outputs = graphicsDevice.SwapchainFramebuffer.OutputDescription;
            return graphicsDevice.ResourceFactory.CreateGraphicsPipeline(pipelineDescription);
        }

        public static Pipeline CreateShaderPipline(GraphicsDevice graphicsDevice, byte[] vert, byte[] frag)
        {
            return CreateShaderPipline(graphicsDevice, LoadShaders(graphicsDevice, vert, frag));
        }



    }
}

