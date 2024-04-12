using System.Numerics;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class Bloom : PostProcess
{
    private const int SampleCount = 2;
    private struct ClampShaderData
    {
        public Vector2 InvFrameSize;
        public float Threshold;
    }

    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandDownSample;
    private readonly GPUCommandBuffer _commandBlit;
    private readonly GPURenderPass _backBufferPass;
    private readonly RenderingSystem _renderingSystem;

    //for clamp
    private readonly Shader _clampShader;
    private readonly GraphicsValueBuffer<ClampShaderData> _clampShaderData;
    private uint _shaderId_data;


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
        _clampShaderData = renderingSystem.CreateGraphicsValueBuffer<ClampShaderData>("bloom_threshold");
        _shaderId_data = clampShader.GetResourceId("data");

        _downSampleShader = downSampleShader;

        _commandDownSample = _device.CreateCommandBuffer();
        _commandBlit = _device.CreateCommandBuffer();
    }

    public override void SetInput(GPUFrameBuffer input)
    {
        base.SetInput(input);

        _tmpFrame?.Dispose();
        _tmpGroup?.Dispose();

        CreateFrameBuffer(input.Width >> 1, input.Height >> 1, "bloom_tmp_frame", out _tmpFrame, out _tmpGroup);

        _clampShaderData.Value = new ClampShaderData
        {
            InvFrameSize = new Vector2(1f) / new Vector2(input.Width, input.Height),
            Threshold = 1.0f
        };
        _clampShaderData.UpdateBuffer();

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

        int downSampleCount = GetDownSampleCount(_tmpFrame.Height);
        _downSampleFrames = new GPUFrameBuffer[downSampleCount];
        _downSampleGroups = new GPUResourceGroup[downSampleCount];

        Log.Info("tmp", _tmpFrame.Width, _tmpFrame.Height);
        for (int i = 0; i < downSampleCount; i++)
        {
            uint width = _tmpFrame.Width >> (i/2 + 1);
            uint height = _tmpFrame.Height >> (i/2 + 1);


            if (i >= downSampleCount - SampleCount)
            {
                float aspectRatio = (float)input.Width / input.Height;
                width = (uint)(_targetDownSampleHeight * aspectRatio);
                height = _targetDownSampleHeight;

                
                CreateFrameBuffer(width, height, $"bloom_down_sample_frame{i}", out _downSampleFrames[i], out _downSampleGroups[i]);
            }
            else
            {
                CreateFrameBuffer(width, height, $"bloom_down_sample_frame{i}", out _downSampleFrames[i], out _downSampleGroups[i]);
            }

            Log.Info(width, height);
        }

        //last height is equal to target height


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
        _commandDownSample.SetGraphicsResources(_shaderId_data, _clampShaderData.EntryReadonly);
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
        return count * SampleCount;
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