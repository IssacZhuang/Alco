using System.Numerics;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class Bloom : PostProcess
{
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandDownSample;
    private readonly GPUCommandBuffer _commandBlit;
    private readonly GPURenderPass _backBufferPass;
    private readonly RenderingSystem _renderingSystem;

    //for clamp
    private readonly Shader _clampShader;
    private readonly GraphicsValueBuffer<float> _threshold;
    private uint _shaderId_threshold;


    private readonly Shader _downSampleShader;
    private readonly uint _targetDownSampleHeight;
    private GPUFrameBuffer? _tmpFrame;//used for HDR clamp
    private GPUResourceGroup? _tmpGroup;
    private GPUFrameBuffer[]? _downSampleFrames;
    private GPUResourceGroup[]? _downSampleGroups;
    internal Bloom(RenderingSystem renderingSystem, Shader blitShader, Shader clampShader, Shader downSampleShader, uint targetDownSampleHeight) : base(renderingSystem, blitShader)
    {
        _device = renderingSystem.GraphicsDevice;
        _renderingSystem = renderingSystem;
        _targetDownSampleHeight = targetDownSampleHeight;

        RenderPassDescriptor descriptor = new RenderPassDescriptor(
            [new(_device.PrefferedHDRFormat)],
            null
        );

        _backBufferPass = _device.CreateRenderPass(descriptor);

        _clampShader = clampShader;
        _threshold = renderingSystem.CreateGraphicsValueBuffer<float>("bloom_threshold");
        _threshold.Value = 1f;
        _threshold.UpdateBuffer();
        _shaderId_threshold = clampShader.GetResourceId("data");

        _downSampleShader = downSampleShader;

        _commandDownSample = _device.CreateCommandBuffer();
        _commandBlit = _device.CreateCommandBuffer();
    }

    public override void SetInput(GPUFrameBuffer input)
    {
        base.SetInput(input);

        _tmpFrame?.Dispose();
        _tmpGroup?.Dispose();

        CreateFrameBuffer(input.Width, input.Height, "bloom_tmp_frame", out _tmpFrame, out _tmpGroup);

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

            CreateFrameBuffer(width, height, $"bloom_down_sample_frame{i}", out _downSampleFrames[i], out _downSampleGroups[i]);
        }

        //last height is equal to target height
        float aspectRatio = (float)input.Width / input.Height;
        uint targetWidth = (uint)(_targetDownSampleHeight * aspectRatio);

        CreateFrameBuffer(targetWidth, _targetDownSampleHeight, $"bloom_down_sample_frame{downSampleCount - 1}", out _downSampleFrames[downSampleCount - 1], out _downSampleGroups[downSampleCount - 1]);

        //print all size

    }

    public override void Blit(GPUFrameBuffer target)
    {
        if (Input == null)
        {
            throw new InvalidOperationException("Input is not set.");
        }

        Mesh mesh = FullScreenMesh;

        _commandDownSample.Begin();
        //clamp
        _commandDownSample.SetFrameBuffer(_tmpFrame!);
        _commandDownSample.SetGraphicsPipeline(_clampShader.Pipeline);
        _commandDownSample.SetVertexBuffer(0, mesh.VertexBuffer);
        _commandDownSample.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        _commandDownSample.SetGraphicsResources(ShaderId_Input, Input!);
        _commandDownSample.SetGraphicsResources(_shaderId_threshold, _threshold.EntryReadonly);
        _commandDownSample.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);

        Vector2 invFrameSize = new Vector2(1f) / new Vector2(_tmpFrame!.Width, _tmpFrame.Height);
        //downsample tmpFrame to first downsample frame
        _commandDownSample.SetFrameBuffer(_downSampleFrames![0]);
        _commandDownSample.SetGraphicsPipeline(_downSampleShader.Pipeline);
        _commandDownSample.SetVertexBuffer(0, mesh.VertexBuffer);
        _commandDownSample.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        _commandDownSample.SetGraphicsResources(ShaderId_Input, _tmpGroup!);
        _commandDownSample.PushConstants(ShaderStage.Fragment, invFrameSize);
        _commandDownSample.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);

        for (int i = 1; i < _downSampleFrames!.Length; i++)
        {
            invFrameSize = new Vector2(1f) / new Vector2(_downSampleFrames[i].Width, _downSampleFrames[i].Height);
            _commandDownSample.SetFrameBuffer(_downSampleFrames[i]);
            _commandDownSample.SetGraphicsPipeline(_downSampleShader.Pipeline);
            _commandDownSample.SetVertexBuffer(0, mesh.VertexBuffer);
            _commandDownSample.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
            _commandDownSample.SetGraphicsResources(ShaderId_Input, _downSampleGroups![i - 1]);
            _commandDownSample.PushConstants(ShaderStage.Fragment, invFrameSize);
            _commandDownSample.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
        }
        _commandDownSample.End();
        _device.Submit(_commandDownSample);

        //blit all downsampled frames to tmpFrame
        _commandBlit.Begin();
        _commandBlit.SetFrameBuffer(target);
        _commandBlit.SetGraphicsPipeline(BlitShader.Pipeline);
        _commandBlit.SetVertexBuffer(0, mesh.VertexBuffer);
        _commandBlit.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
        for (int i = 0; i < _downSampleFrames.Length; i++)
        {
            _commandBlit.SetGraphicsResources(ShaderId_Input, _downSampleGroups![i]);
            _commandBlit.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
        }
        _commandBlit.End();
        _device.Submit(_commandBlit);
    }

    private void CreateFrameBuffer(uint width, uint height, string name, out GPUFrameBuffer frameBuffer, out GPUResourceGroup group)
    {
        FrameBufferDescriptor desc = new FrameBufferDescriptor(
            _backBufferPass,
            width,
            height,
            name
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
        _commandDownSample.Dispose();
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