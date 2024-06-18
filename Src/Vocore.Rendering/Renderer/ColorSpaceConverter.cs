using Vocore.Graphics;

namespace Vocore.Rendering;

public class ColorSpaceConverter : AutoDisposable
{
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
    private readonly Shader _shader;

    private readonly Mesh _mesh;
    private readonly uint _shaderId_input;

    private GPUFrameBuffer? _input;
    private GPUResourceGroup? _inputGroup;
    private GPURenderPass? _renderPass;
    private GPUPipeline? _pipeline;


    internal ColorSpaceConverter(RenderingSystem renderingSystem, Shader toneMapShader)
    {
        if (!toneMapShader.IsGraphicsShader)
        {
            throw new InvalidOperationException("ToneMap shader must be a graphics shader.");
        }

        _device = renderingSystem.GraphicsDevice;
        _shader = toneMapShader;

        _mesh = renderingSystem.MeshFullScreen;
        _shaderId_input = _shader.GetResourceId("_texture");

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

        if (_renderPass != target.RenderPass)
        {
            _renderPass = target.RenderPass;
            _pipeline = _shader.GetPipelineVariant(_renderPass);
        }

        _command.Begin();
        _command.SetFrameBuffer(target);
        _command.SetGraphicsPipeline(_pipeline!);
        _command.SetVertexBuffer(0, _mesh.VertexBuffer);
        _command.SetIndexBuffer(_mesh.IndexBuffer, _mesh.IndexFormat);
        _command.SetGraphicsPipeline(_pipeline!);
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
        //dispose private managed resources
        _inputGroup?.Dispose();
        _command.Dispose();
    }
}