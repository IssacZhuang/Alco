using Vocore.Graphics;

namespace Vocore.Rendering;

public class Bloom : PostProcess
{
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
    private readonly GPURenderPass _backBufferPass;
    private readonly RenderingSystem _renderingSystem;

    //for clamp
    private readonly Shader _clampShader;
    private readonly GraphicsValueBuffer<float> _threshold;
    private uint _shaderId_threshold;


    private readonly Shader _downSampleShader;
    private readonly uint _targetDownSampleHeight;
    private GPUFrameBuffer? _tmpFrame;//used for HDR clamp
    private GPUFrameBuffer[]? _downSampleFrames;
    private GPUResourceGroup[]? _downSampleGroups;
    internal Bloom(RenderingSystem renderingSystem, Shader blitShader, Shader clampShader, Shader downSampleShader, uint targetDownSampleHeight) : base(renderingSystem, blitShader)
    {
        _device = renderingSystem.GraphicsDevice;
        _renderingSystem = renderingSystem;
        _targetDownSampleHeight = targetDownSampleHeight;

        RenderPassDescriptor descriptor = new RenderPassDescriptor(
            [new(PixelFormat.RGBA16Float)],
            null
        );

        _backBufferPass = _device.CreateRenderPass(descriptor);

        _clampShader = clampShader;
        _threshold = renderingSystem.CreateGraphicsValueBuffer<float>("bloom_threshold");
        _threshold.Value = 1f;
        _threshold.UpdateBuffer();
        _shaderId_threshold = clampShader.GetResourceId("data");

        _downSampleShader = downSampleShader;

        _command = _device.CreateCommandBuffer();
    }

    public override void SetInput(GPUFrameBuffer input)
    {
        base.SetInput(input);

        _tmpFrame?.Dispose();

        FrameBufferDescriptor _tmpFrameDesc = new FrameBufferDescriptor(
           _backBufferPass,
           input.Width,
           input.Height
        );

        _tmpFrame = _device.CreateFrameBuffer(_tmpFrameDesc);

        //dispose old downsample framebuffers
        if (_downSampleFrames != null)
        {
            for (int i = 0; i < _downSampleFrames.Length; i++)
            {
                _downSampleFrames[i].Dispose();
            }
        }

        if (_downSampleGroups != null)
        {
            for (int i = 0; i < _downSampleGroups.Length; i++)
            {
                _downSampleGroups[i].Dispose();
            }
        }

        int downSampleCount = GetDownSampleCount(input.Height);
        _downSampleFrames = new GPUFrameBuffer[downSampleCount];
        _downSampleGroups = new GPUResourceGroup[downSampleCount];

        for (int i = 0; i < downSampleCount - 1; i++)
        {
            uint width = input.Width >> i;
            uint height = input.Height >> i;

            CreateDownSampleFrameBuffer( width, height, out _downSampleFrames[i], out _downSampleGroups[i]);
        }

        //last height is equal to target height
        float aspectRatio = (float)input.Width / input.Height;
        uint targetWidth = (uint)(_targetDownSampleHeight * aspectRatio);

        CreateDownSampleFrameBuffer( targetWidth, _targetDownSampleHeight, out _downSampleFrames[downSampleCount - 1], out _downSampleGroups[downSampleCount - 1]);
    }

    public override void Blit(GPUFrameBuffer target)
    {
        if (Input == null)
        {
            throw new InvalidOperationException("Input is not set.");
        }

        Mesh mesh = FullScreenMesh;

        _command.Begin();
        //clamp
        _command.SetFrameBuffer(_tmpFrame!);
        _command.SetGraphicsPipeline(_clampShader.Pipeline);
        _command.SetVertexBuffer(0, mesh.VertexBuffer);
        _command.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        _command.SetGraphicsResources(ShaderId_Input, Input!);
        _command.SetGraphicsResources(_shaderId_threshold, _threshold.EntryReadonly);
        _command.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
        _command.End();
        _device.Submit(_command);
    }

    private void CreateDownSampleFrameBuffer( uint width, uint height, out GPUFrameBuffer frameBuffer, out GPUResourceGroup group)
    {
        FrameBufferDescriptor desc = new FrameBufferDescriptor(
            _backBufferPass,
            width,
            height
        );

        frameBuffer = _device.CreateFrameBuffer(desc);

        ResourceGroupDescriptor groupDescriptor = new ResourceGroupDescriptor(
            _device.BindGroupTexture2DSampled,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, frameBuffer.ColorViews[0]),
                new ResourceBindingEntry(1, _device.SamplerLinearClamp)
            }
        );

        group = _device.CreateResourceGroup(groupDescriptor);
    }

    private int GetDownSampleCount(uint height)
    {
        int count = 0;
        while (height > _targetDownSampleHeight)
        {
            height >>= 1;
            count++;
        }
        return count;
    }

    protected override void Dispose(bool disposing)
    {
        _command.Dispose();
        _tmpFrame?.Dispose();
        if (_downSampleFrames != null)
        {
            for (int i = 0; i < _downSampleFrames.Length; i++)
            {
                _downSampleFrames[i].Dispose();
            }
        }

        if (_downSampleGroups != null)
        {
            for (int i = 0; i < _downSampleGroups.Length; i++)
            {
                _downSampleGroups[i].Dispose();
            }
        }
    }
}