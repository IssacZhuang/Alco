using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Input;
using Vocore.Graphics;
using Vocore.Engine;
using Vocore;

public class Game : GameEngine
{
    #region Shader Data

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector2 Position;
        public Vector2 TexCoord;
    }

    private static readonly Vertex[] Vertices =
    {
        new Vertex {Position = new Vector2(-0.5f, 0.5f), TexCoord = new Vector2(0, 0)},
        new Vertex {Position = new Vector2(0.5f, 0.5f), TexCoord = new Vector2(1, 0)},
        new Vertex {Position = new Vector2(0.5f, -0.5f), TexCoord = new Vector2(1, 1)},
        new Vertex {Position = new Vector2(-0.5f, -0.5f), TexCoord = new Vector2(0, 1)}
    };

    private static readonly ushort[] Indices = { 0, 1, 2, 0, 2, 3 };

    #endregion

    private Camera2D camera;

    private GPUCommandBuffer _commandBuffer;
    private GPUPipeline _pipeline;
    private Texture2D _texBlue;
    private Texture2D _texRed;
    private Texture2D _texGreen;



    private float _timer = 0.0f;

    public Game(GameEngineSetting setting) : base(setting)
    {
        Transform2D transform = new Transform2D(Vector2.One*2, Rotation2D.CreateByDegree(45), Vector2.One*3);
        Log.Info(GetMatrixString(transform.MatrixTranslation));
        Log.Info(GetMatrixString(transform.MatrixRotation));
        Log.Info(GetMatrixString(Matrix3x2.CreateRotation(math.radians(45))));
        Log.Info(GetMatrixString(transform.MatrixScale));
        Log.Info(GetMatrixString(transform.Matrix));
        Log.Info(Vector2.Transform(new Vector2(3, 3), transform.Matrix));



        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
        _pipeline = CreatePipeline();

        _texBlue = RenderingService.CreateTexture2DEmpty(16, 16, new Vector4(0, 0, 1, 1));
        _texRed = RenderingService.CreateTexture2DEmpty(16, 16, new Vector4(1, 0, 0, 1));
        _texGreen = RenderingService.CreateTexture2DEmpty(16, 16, new Vector4(0, 1, 0, 1));

        camera = new Camera2D();



    }

    private static string GetMatrixString(Matrix3x2 matrix)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"[ {matrix.M11}, {matrix.M12}");
        sb.AppendLine($"  {matrix.M21}, {matrix.M22}");
        sb.AppendLine($"  {matrix.M31}, {matrix.M32} ]");
        return sb.ToString();
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(Key.Escape))
        {
            Stop();
        }
    }

    protected override void OnDraw(float delta)
    {
        _timer += delta;

    }

    protected override void OnStop()
    {
        _commandBuffer.Dispose();
        _texBlue.Dispose();
        _texRed.Dispose();
        _texGreen.Dispose();
    }

    private GPUPipeline CreatePipeline()
    {
        string shaderCode = Encoding.UTF8.GetString(LoadFile("Shader.glsl"));
        ShaderStageSource vertSource = ShaderCompilerShaderc.CrearteSpirvSourceFromGlsl(shaderCode, ShaderStage.Vertex, "main", "Shader.glsl");
        ShaderStageSource fragSource = ShaderCompilerShaderc.CrearteSpirvSourceFromGlsl(shaderCode, ShaderStage.Fragment, "main", "Shader.glsl");

        ShaderReflectionInfo info = UtilsShaderRelfection.GetSpirvReflection(vertSource.Source, fragSource.Source);

        GPUBindGroup[] bindGroups = new GPUBindGroup[info.BindGroups.Length];
        for (int i = 0; i < info.BindGroups.Length; i++)
        {
            bindGroups[i] = GraphicsDevice.CreateBindGroup(info.BindGroups[i].ToDescriptor());
        }

        RasterizerState rasterizer = RasterizerState.CullNone;
        BlendState blend = BlendState.NonPremultipliedAlpha;
        DepthStencilState depthStencil = DepthStencilState.DepthNone;

        GraphicsPipelineDescriptor descriptor = new GraphicsPipelineDescriptor(
            bindGroups,
            new ShaderStageSource[] { vertSource, fragSource },
            info.VertexLayouts,
            rasterizer,
            blend,
            depthStencil,
            new PixelFormat[] { GraphicsDevice.PrefferedSurfaceFomat },
            GraphicsDevice.PrefferedDepthStencilFormat,
            "quad_pipline"
        );

        return GraphicsDevice.CreateGraphicsPipeline(descriptor);
    }

    private static byte[] LoadFile(string path)
    {
        return File.ReadAllBytes(Path.Combine("Assets", path));
    }
}