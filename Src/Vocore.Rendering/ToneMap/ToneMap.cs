using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class ToneMap
{
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
    private readonly Shader _shader;
    private readonly uint _shaderId_input;
    private readonly uint _shaderId_output;
    private GPUFrameBuffer? _input;
    private GPUFrameBuffer? _output;

    private GPUTextureView? _inputView;
    private GPUTextureView? _outputView;

    private GPUResourceGroup? _inputGroup;
    private GPUResourceGroup? _outputGroup;

    internal ToneMap(GPUDevice device, Shader toneMapComputeShader)
    {
        if(!toneMapComputeShader.IsComputeShader)
        {
            throw new InvalidOperationException("ToneMap shader must be a compute shader.");
        }

        _device = device;
        _shader = toneMapComputeShader;
        _shaderId_input = _shader.GetResourceId("input");
        _shaderId_output = _shader.GetResourceId("output");

        _command = device.CreateCommandBuffer();
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
            _device.BindGroupTexture2DRead,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, _inputView),
            }
        );

        _inputGroup = _device.CreateResourceGroup(groupDescriptor);
    }

    public virtual void SetOutput(GPUFrameBuffer output)
    {
        _outputView?.Dispose();
        _outputGroup?.Dispose();

        _output = output;
        TextureViewDescriptor viewDescriptor = new TextureViewDescriptor
        {
            Texture = output.Colors[0],
            Dimension = TextureViewDimension.Texture2D,
            BaseArrayLayer = 0,
            BaseMipLevel = 0,
            ArrayLayerCount = 1,
            MipLevelCount = 1
        };

        _outputView = _device.CreateTextureView(viewDescriptor);

        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _device.BindGroupStorageTexture2D,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, _outputView),
            }
        );

        _outputGroup = _device.CreateResourceGroup(groupDescriptor);
    }

    public virtual void Execute()
    {
        if (_input == null)
        {
            throw new InvalidOperationException("Input is not set.");
        }

        if (_output == null)
        {
            throw new InvalidOperationException("Output is not set.");
        }

        _command.Begin();
        _command.SetComputePipeline(_shader.Pipeline);
        _command.SetComputeResources(_shaderId_input, _inputGroup!);
        _command.SetComputeResources(_shaderId_output, _outputGroup!);
        _command.DispatchCompute(_output!.Width, _output!.Height, 1);
        _command.End();
    }
}