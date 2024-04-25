using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class ToneMap:AutoDisposable
{
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
    private readonly Shader _shader;
    private readonly GPUPipeline _pipeline;
    private readonly Mesh _mesh;
    private readonly uint _shaderId_input;

    private GPUFrameBuffer? _input;
    private GPUResourceGroup? _inputGroup;


    public Shader Shader => _shader;

    internal ToneMap(RenderingSystem renderingSystem, Shader toneMapShader)
    {
        if (!toneMapShader.IsGraphicsShader)
        {
            throw new InvalidOperationException("ToneMap shader must be a graphics shader.");
        }

        _device = renderingSystem.GraphicsDevice;
        _shader = toneMapShader;
        _pipeline = toneMapShader.GetPipelineVariant(_device.SwapChainRenderPass);
        _mesh = renderingSystem.MeshFullScreen;
        _shaderId_input = _shader.GetResourceId("texture");

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

        _command.Begin();
        _command.SetFrameBuffer(target);
        _command.SetGraphicsPipeline(_shader.DefaultPipeline);
        _command.SetVertexBuffer(0,_mesh.VertexBuffer);
        _command.SetIndexBuffer(_mesh.IndexBuffer, _mesh.IndexFormat);
        _command.SetGraphicsPipeline(_shader.DefaultPipeline);
        _command.SetGraphicsResources(_shaderId_input, _inputGroup!);
        OnSetGraphicsResources(_command);
        _command.DrawIndexed(_mesh.IndexCount, 1, 0, 0, 0);
        _command.End();
        _device.Submit(_command);
    }

    protected virtual void OnSetGraphicsResources(GPUCommandBuffer command)
    {
        
    }

    protected override void Dispose(bool disposing)
    {
        _inputGroup?.Dispose();
        _command.Dispose();
    }
}