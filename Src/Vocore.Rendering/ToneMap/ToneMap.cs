using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class ToneMap:AutoDisposable
{
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
    private readonly Shader _shader;
    private readonly Mesh _mesh;
    private readonly uint _shaderId_input;

    private GPUFrameBuffer? _input;
    private GPUTextureView? _inputView;
    private GPUResourceGroup? _inputGroup;


    public Shader Shader => _shader;

    internal ToneMap(RenderingSystem renderingSystem, Shader toneMapShader)
    {
        if (!toneMapShader.IsGraphicsShader)
        {
            throw new InvalidOperationException("ToneMap shader must be a compute shader.");
        }

        _device = renderingSystem.GraphicsDevice;
        _shader = toneMapShader;
        _mesh = renderingSystem.FullScreenMesh;
        _shaderId_input = _shader.GetResourceId("texture");

        _command = _device.CreateCommandBuffer();
    }

    public virtual void SetInput(GPUFrameBuffer input)
    {
        _inputView?.Dispose();
        _inputGroup?.Dispose();

        _input = input;
        TextureViewDescriptor viewDescriptor = new TextureViewDescriptor
        {
            Texture = input.Colors[0],
            Dimension = TextureViewDimension.Texture2D,
            BaseArrayLayer = 0,
            BaseMipLevel = 0,
            ArrayLayerCount = 1,
            MipLevelCount = 1
        };

        _inputView = _device.CreateTextureView(viewDescriptor);

        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _device.BindGroupTexture2DSampled,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, _inputView),
                new ResourceBindingEntry(1, _device.SamplerNearestClamp)
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
        _command.SetGraphicsPipeline(_shader.Pipeline);
        _command.SetVertexBuffer(0,_mesh.VertexBuffer);
        _command.SetIndexBuffer(_mesh.IndexBuffer, _mesh.IndexFormat);
        _command.SetGraphicsPipeline(_shader.Pipeline);
        _command.SetGraphicsResources(_shaderId_input, _inputGroup!);
        OnSetComptueResources(_command);
        _command.DrawIndexed(_mesh.IndexCount, 1, 0, 0, 0);
        _command.End();
        _device.Submit(_command);
    }

    protected virtual void OnSetComptueResources(GPUCommandBuffer command)
    {
        
    }

    protected override void Dispose(bool disposing)
    {
        _inputView?.Dispose();
        _inputGroup?.Dispose();
        _command.Dispose();
    }
}