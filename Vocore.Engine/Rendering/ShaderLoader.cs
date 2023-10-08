using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{

    public static class ShaderLoader
    {
        public const string EntryPointVertex = "vertex";
        public const string EntryPointFragment = "fragment";


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

        public static Shader[] LoadShaders(GraphicsDevice graphicsDevice, byte[] text)
        {
            return LoadShaders(graphicsDevice, PreprocessVertexShader(text), PreprocessFragmentShader(text));
        }

        public static Shader[] LoadShaders(GraphicsDevice graphicsDevice, string text)
        {
            return LoadShaders(graphicsDevice, PreprocessVertexShader(text), PreprocessFragmentShader(text));
        }

        public static Pipeline CreateShaderPipline(GraphicsDevice graphicsDevice, Shader[] shaders)
        {
            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription();

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



    }
}

