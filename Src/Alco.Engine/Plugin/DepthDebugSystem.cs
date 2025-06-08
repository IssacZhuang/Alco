
using System.Numerics;
using System.Text;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

public class DepthDebugSystem : BaseEngineSystem
{
    private readonly Mesh _mesh;
    private readonly ViewRenderTarget _renderTarget;
    private readonly GPUDevice _device;
    private readonly GPUPipeline _pipeline;

    public override int Order => 2000;

    public DepthDebugSystem(GameEngine engine, ViewRenderTarget renderTarget)
    {
        _renderTarget = renderTarget;

        RenderingSystem rendering = engine.RenderingSystem;
        _device = rendering.GraphicsDevice;


        if (!engine.AssetSystem.TryLoadRaw(BuiltInAssetsPath.Shader_BlitDepth, out SafeMemoryHandle shaderContent))
        {
            throw new Exception("Failed to load shader");
        }

        string shaderText = Encoding.UTF8.GetString(shaderContent.AsReadOnlySpan());

        var bindingLayout = new BindGroupLayout[]{
            new BindGroupLayout{
                Group = 0,
                Bindings = new BindGroupEntryInfo[]{
                    new BindGroupEntryInfo{
                        Entry = new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, TextureBindingInfo.Depth2D),
                        Size = 0
                    },
                    new BindGroupEntryInfo{
                        Entry = new BindGroupEntry(1, ShaderStage.Standard, BindingType.Sampler),
                        Size = 0
                    },
                }
            }
        };




        _mesh = rendering.MeshFullScreen;

        _pipeline = CreatePipeline();

    }

    public override void OnPostUpdate(float delta)
    {
        // _renderer.Begin(_renderTarget.FrameBuffer);
        // _renderer.Draw(_mesh, _material);
        // _renderer.End();
    }

    public override void Dispose()
    {

    }

    private unsafe GPUPipeline CreatePipeline()
    {
        byte[] ShaderCode = Encoding.UTF8.GetBytes(Shader_BlitDepth);
        ShaderModule vertexShader = new ShaderModule(ShaderStage.Vertex, ShaderLanguage.WGSL, ShaderCode, "MainVS");
        ShaderModule fragmentShader = new ShaderModule(ShaderStage.Fragment, ShaderLanguage.WGSL, ShaderCode, "MainPS");

        VertexInputLayout vertexLayout = new VertexInputLayout
        {
            StepMode = VertexStepMode.Vertex,
            Stride = (uint)sizeof(Vertex),
            Elements = new VertexElement[]
            {
                new VertexElement(0, 0, VertexFormat.Float32x3, "Position"),
                new VertexElement(1, (uint)(sizeof(Vector2)), VertexFormat.Float32x2, "TexCoord"),
            }
        };

        RasterizerState rasterizer = RasterizerState.CullNone;
        BlendState blend = BlendState.NonPremultipliedAlpha;
        DepthStencilState depthStencil = DepthStencilState.Default;

        GPURenderPass renderPass = _renderTarget.FrameBuffer.RenderPass;

        GraphicsPipelineDescriptor pipelineDescriptor = new GraphicsPipelineDescriptor(
            new GPUBindGroup[] { _device.BindGroupTextureDepthSampled },
            new ShaderModule[] { vertexShader, fragmentShader },
            new VertexInputLayout[] { vertexLayout },
            rasterizer,
            blend,
            depthStencil,
            new PixelFormat[] { renderPass.Colors[0].Format },
             renderPass.Depth.HasValue ? renderPass.Depth.Value.Format : null,
            null,
            "draw_depth_pipeline"
        );

        return _device.CreateGraphicsPipeline(pipelineDescriptor);
    }

    public const string Shader_BlitDepth = @"
@group(0) @binding(0) var _texture: texture_depth_2d;
@group(0) @binding(1) var _textureSampler: sampler;

struct VertexInput {
  @location(0) position: vec3<f32>,
  @location(1) uv: vec2<f32>,
};

struct V2F {
  @builtin(position) position: vec4<f32>,
  @location(0) uv: vec2<f32>,
};

@vertex
fn MainVS(input: VertexInput) -> V2F {
  var output: V2F;
  output.position = vec4<f32>(input.position, 1.0);
  output.uv = input.uv;
  return output;
}

@fragment
fn MainPS(input: V2F) -> @location(0) vec4<f32> {
  let depth = textureSample(_texture, _textureSampler, input.uv);
  return vec4<f32>(depth, depth, depth, 1.0);
} 
    ";
}

