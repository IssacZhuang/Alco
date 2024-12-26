using Vocore.Graphics;

namespace Vocore.Rendering;

//todo: use render texture
public class ColorSpaceConverter : AutoDisposable
{
    public const string ShaderId_texture = "_texture";

    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUCommandBuffer _command;
    private readonly Shader _shader;

    private readonly Mesh _mesh;
    private uint _shaderId_input;

    private GPUFrameBuffer? _input;
    private GPUResourceGroup? _inputGroup;
    private GraphicsPipelineContext _pipelineContext;

    internal ColorSpaceConverter(RenderingSystem renderingSystem, Shader toneMapShader)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _shader = toneMapShader;

        _mesh = renderingSystem.MeshFullScreen;

        _pipelineContext = GraphicsPipelineContext.Default;

        _command = _device.CreateCommandBuffer();
    }

    public virtual void SetInput(GPUFrameBuffer input)
    {
        _inputGroup?.Dispose();

        _input = input;


        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _device.BindGroupTexture2DSampled,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, _input.ColorViews[0]),
                new ResourceBindingEntry(1, _device.SamplerLinearClamp)
            }
        );

        _inputGroup = _device.CreateResourceGroup(groupDescriptor);
    }

    public void Blit(GPUFrameBuffer target)
    {
        if (_input == null)
        {
            throw new InvalidOperationException("Input is not set.");
        }

        if (_shader.TryUpdatePipelineContext(ref _pipelineContext, target.RenderPass))
        {
            _shaderId_input = _pipelineContext.GetResourceId(ShaderId_texture);
            OnUpdatePipeline(_pipelineContext);
        }

        _command.Begin();
        _command.SetFrameBuffer(target);
        _command.SetGraphicsPipeline(_pipelineContext);
        _command.SetVertexBuffer(0, _mesh.VertexBuffer);
        _command.SetIndexBuffer(_mesh.IndexBuffer, _mesh.IndexFormat);
        _command.SetGraphicsResources(_shaderId_input, _inputGroup!);
        OnSetGraphicsResources(_command);
        _command.DrawIndexed(_mesh.IndexCount, 1, 0, 0, 0);
        _command.End();
        _renderingSystem.ScheduleCommandBuffer(_command);
    }

    protected virtual void OnSetGraphicsResources(GPUCommandBuffer command)
    {

    }

    protected virtual void OnUpdatePipeline(GraphicsPipelineContext pipelineInfo)
    {

    }

    protected override void Dispose(bool disposing)
    {
        //dispose private managed resources
        _inputGroup?.Dispose();
        _command.Dispose();
    }
}